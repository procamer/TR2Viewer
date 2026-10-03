using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using TR2Viewer.Models;

namespace TR2Viewer.Render
{
    public class TRViewer(int width, int height, string title, TR2Level level) : 
        GameWindow(GameWindowSettings.Default, new NativeWindowSettings() { ClientSize = (width, height), Title = title, NumberOfSamples = 8 })
    {
        private int _vao;
        private int _vbo;
        private int _vertexCount;
        private int _textureArray;
        private int _colorPage;            // Renkli yüzler için Palette16'dan üretilen ek doku katmanı

        private Vector3 _cameraPosition;
        private Vector3 _cameraFront = new(0.0f, 0.0f, -1.0f);
        private Vector3 _cameraUp = Vector3.UnitY;

        private float _yaw = -90.0f;
        private float _pitch = 0.0f;
        private Vector2 _lastMousePos;
        private bool _firstMouse = true;

        private int _currentRoom = 0;
        private float _velocityY = 0f;
        private const float EyeHeight = 700f / 1024f; // Lara'nın göz hizası (TR birimi 700)

        // Flipmap: bölümde bir olay olunca normal odanın yerini alan "alternatif" odalar.
        // Varsayılan durumda çizilmezler, yoksa normal odayla üst üste biner.
        private readonly HashSet<int> _alternateRooms = [];
        private bool _noclip = true;
        private bool _nKeyPressed = false;

        protected override void OnLoad()
        {
            base.OnLoad();
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.Multisample);
            GL.Enable(EnableCap.SampleAlphaToCoverage);

            foreach (var room in level.Rooms)
            {
                if (room.AlternateRoom >= 0) _alternateRooms.Add(room.AlternateRoom);
            }

            LoadTextures();
            BuildMapGeometry();
            TRShader.CompileShaders();

            bool laraFound = false;
            if (level.Entities != null)
            {
                foreach (var ent in level.Entities)
                {
                    if (ent.TypeID == 0) // Lara Croft
                    {
                        // Entity Y değeri ayak hizasıdır; kamerayı Lara'nın göz hizasına (~700 birim) kaldır
                        _cameraPosition = new Vector3(ent.X / 1024f, -ent.Y / 1024f + EyeHeight, -ent.Z / 1024f);
                        float angleDeg = (ent.Angle / 32768f) * 180f;
                        _yaw = angleDeg - 90f;
                        _currentRoom = ent.Room;
                        laraFound = true;
                        break;
                    }
                }
            }
            if (!laraFound && level.Rooms.Length > 0)
            {
                _cameraPosition = new Vector3((level.Rooms[0].Info.X / 1024f) + 3f, (-level.Rooms[0].Info.YTop / 1024f) - 3f, (-level.Rooms[0].Info.Z / 1024f) - 3f);
            }
            
            Console.WriteLine($"Yüklendi: {level.Rooms.Length} oda, {_vertexCount / 3} üçgen, kamera {_cameraPosition}");

            CursorState = CursorState.Grabbed;
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, e.Width, e.Height);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.UseProgram(TRShaderHelpers._shaderProgram);

            var view = Matrix4.LookAt(_cameraPosition, _cameraPosition + _cameraFront, _cameraUp);
            var projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), Size.X / (float)Size.Y, 0.1f, 1000.0f);

            int viewLoc = GL.GetUniformLocation(TRShaderHelpers._shaderProgram, "view");
            GL.UniformMatrix4(viewLoc, false, ref view);

            int projLoc = GL.GetUniformLocation(TRShaderHelpers._shaderProgram, "projection");
            GL.UniformMatrix4(projLoc, false, ref projection);

            // Doku filtreleri LoadTextures içinde bir kez ayarlanır; her karede tekrar etmeye gerek yok
            GL.BindTexture(TextureTarget.Texture2DArray, _textureArray);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);


            SwapBuffers();
        }

        protected override void OnUnload()
        {
            // OpenGL kaynaklarını serbest bırak
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            GL.DeleteTexture(_textureArray);
            GL.DeleteProgram(TRShaderHelpers._shaderProgram);
            base.OnUnload();
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);
            var input = KeyboardState;

            if (input.IsKeyDown(Keys.Escape)) Close();

            // Kamera Hareketi
            float cameraSpeed = 5.0f * (float)e.Time;
            Vector3 moveDir = Vector3.Zero;

            if (input.IsKeyDown(Keys.W)) moveDir += _cameraFront;
            if (input.IsKeyDown(Keys.S)) moveDir -= _cameraFront;
            if (input.IsKeyDown(Keys.A)) moveDir -= Vector3.Normalize(Vector3.Cross(_cameraFront, _cameraUp));
            if (input.IsKeyDown(Keys.D)) moveDir += Vector3.Normalize(Vector3.Cross(_cameraFront, _cameraUp));
            if (_noclip)
            {
                if (input.IsKeyDown(Keys.Space)) moveDir += _cameraUp;
                if (input.IsKeyDown(Keys.LeftShift)) moveDir -= _cameraUp;
            }
            else
            {                
                moveDir.Y = 0; // Yerçekimi açıkken sadece X ve Z ekseninde yürü, havaya uçma
            }

            // Hareketi uygula ve hangi odaya girdiğimizi kontrol et
            if (moveDir != Vector3.Zero)
            {
                Vector3 velocity = Vector3.Normalize(moveDir) * cameraSpeed;
                Vector3 nextPosition = _cameraPosition + velocity;
                if (_noclip)
                {
                    _cameraPosition = nextPosition; // Uçarken engel tanıma
                }
                else
                {
                    // Kapı (portal) sektörüne adım atılırsa önce komşu odaya geç, duvar kontrolünü o odada yap
                    int roomX = ResolvePortals(_currentRoom, nextPosition.X, _cameraPosition.Z);
                    if (!IsWall(roomX, nextPosition.X, _cameraPosition.Z))
                    {
                        _cameraPosition.X = nextPosition.X;
                        _currentRoom = roomX;
                    }

                    int roomZ = ResolvePortals(_currentRoom, _cameraPosition.X, nextPosition.Z);
                    if (!IsWall(roomZ, _cameraPosition.X, nextPosition.Z))
                    {
                        _cameraPosition.Z = nextPosition.Z;
                        _currentRoom = roomZ;
                    }
                }

                if (_noclip) UpdateCurrentRoom();
            }

            // Noclip Aç/Kapat (Debounce ile)
            bool isNPressed = input.IsKeyDown(Keys.N);
            if (isNPressed && !_nKeyPressed)
            {
                _noclip = !_noclip;
                _velocityY = 0f;
                Console.WriteLine("Noclip Modu: " + (_noclip ? "AÇIK (Uçuyorsun)" : "KAPALI (Yerçekimi devrede)"));
            }
            _nKeyPressed = isNPressed;

            // FİZİK VE YERÇEKİMİ
            if (!_noclip)
            {
                // 1. Yerçekimi İvmesi (Aşağı doğru çekim)
                _velocityY -= 15.0f * (float)e.Time;
                _cameraPosition.Y += _velocityY * (float)e.Time;

                // Açık zeminden aşağı düştüysek veya açık tavandan yukarı çıktıysak alt/üst odaya geç
                _currentRoom = ResolveVertical(_currentRoom, _cameraPosition.X, _cameraPosition.Y - EyeHeight, _cameraPosition.Z);

                float floorHeight = GetFloorHeight(_currentRoom, _cameraPosition.X, _cameraPosition.Z);

                // 2. Zemine Çarpma Kontrolü
                if (_cameraPosition.Y < floorHeight + EyeHeight && floorHeight != -9999f)
                {
                    _cameraPosition.Y = floorHeight + EyeHeight;

                    // Eğer yerdeysek ve Space'e basılırsa ZIPLA!
                    if (input.IsKeyDown(Keys.Space))
                    {
                        _velocityY = 6.0f; // Zıplama gücü (İstersen artırabilirsin)
                    }
                    else
                    {
                        _velocityY = 0f; // Sadece yerdeyken hızı sıfırla
                    }
                }
            }

            // Fare Kontrolleri
            var mouse = MouseState;
            if (_firstMouse)
            {
                _lastMousePos = new Vector2(mouse.X, mouse.Y);
                _firstMouse = false;
            }

            float deltaX = mouse.X - _lastMousePos.X;
            float deltaY = mouse.Y - _lastMousePos.Y;
            _lastMousePos = new Vector2(mouse.X, mouse.Y);

            float sensitivity = 0.1f;
            _yaw += deltaX * sensitivity;
            _pitch -= deltaY * sensitivity;

            if (_pitch > 89.0f) _pitch = 89.0f;
            if (_pitch < -89.0f) _pitch = -89.0f;

            _cameraFront.X = (float)Math.Cos(MathHelper.DegreesToRadians(_pitch)) * (float)Math.Cos(MathHelper.DegreesToRadians(_yaw));
            _cameraFront.Y = (float)Math.Sin(MathHelper.DegreesToRadians(_pitch));
            _cameraFront.Z = (float)Math.Cos(MathHelper.DegreesToRadians(_pitch)) * (float)Math.Sin(MathHelper.DegreesToRadians(_yaw));
            _cameraFront = Vector3.Normalize(_cameraFront);
        }

        private void LoadTextures()
        {
            _textureArray = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2DArray, _textureArray);
            // Son katman renk sayfasıdır (renkli yüzler için)
            _colorPage = (int)level.NumTextiles;
            GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, 256, 256, _colorPage + 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            for (int i = 0; i < level.NumTextiles; i++)
            {
                byte[] rgba = new byte[256 * 256 * 4];
                for (int j = 0; j < 65536; j++)
                {
                    ushort p = level.Textiles16[i].Tile[j];
                    // ARGB1555: 5 bitlik kanalı 8 bite genişlet (31 -> 255), saydamlık sadece A bitinden gelir
                    rgba[j * 4 + 0] = Expand5(p >> 10);
                    rgba[j * 4 + 1] = Expand5(p >> 5);
                    rgba[j * 4 + 2] = Expand5(p);
                    rgba[j * 4 + 3] = (byte)((p & 0x8000) != 0 ? 255 : 0);
                }
                GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, i, 256, 256, 1, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            }

            // Renk sayfası: 256 Palette16 rengini 16x16 piksellik hücrelere yerleştir (bkz. PaletteUV)
            byte[] colors = new byte[256 * 256 * 4];
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    var c = level.Palette16[(y / 16) * 16 + (x / 16)];
                    int o = (y * 256 + x) * 4;
                    colors[o + 0] = c.R;
                    colors[o + 1] = c.G;
                    colors[o + 2] = c.B;
                    colors[o + 3] = 255;
                }
            }
            GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, _colorPage, 256, 256, 1, PixelFormat.Rgba, PixelType.UnsignedByte, colors);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }

        private static byte Expand5(int value)
        {
            int c = value & 0x1F;
            return (byte)((c << 3) | (c >> 2));
        }

        // TR2 ışık değerleri ters ölçeklidir: 0 = en parlak, 0x1FFF (8191) = en karanlık
        private static float ShadeToLight(int shade)
        {
            float light = 1.0f - (shade / 8192f);
            return Math.Clamp(light, 0.15f, 1.0f); // Minimum %15 her zaman görünür kalsın
        }

        private void BuildMapGeometry()
        {
            List<float> vertices = [];

            // 1. ODALAR (Sabit Duvarlar ve Zeminler)
            for (int r = 0; r < level.Rooms.Length; r++)
            {
                var room = level.Rooms[r];
                if (room.Vertices == null || _alternateRooms.Contains(r)) continue;
                if (room.Rectangles != null)
                {
                    foreach (var rect in room.Rectangles)
                    {
                        var objTex = level.ObjectTextures[rect.Texture & 0x7FFF];
                        int page = objTex.TileAndFlag & 0x00FF;

                        AddVertex(vertices, room, rect.V1, objTex.U[0], objTex.V[0], page);
                        AddVertex(vertices, room, rect.V2, objTex.U[1], objTex.V[1], page);
                        AddVertex(vertices, room, rect.V3, objTex.U[2], objTex.V[2], page);

                        AddVertex(vertices, room, rect.V1, objTex.U[0], objTex.V[0], page);
                        AddVertex(vertices, room, rect.V3, objTex.U[2], objTex.V[2], page);
                        AddVertex(vertices, room, rect.V4, objTex.U[3], objTex.V[3], page);
                    }
                }
                if (room.Triangles != null)
                {
                    foreach (var tri in room.Triangles)
                    {
                        var objTex = level.ObjectTextures[tri.Texture & 0x7FFF];
                        int page = objTex.TileAndFlag & 0x00FF;

                        AddVertex(vertices, room, tri.V1, objTex.U[0], objTex.V[0], page);
                        AddVertex(vertices, room, tri.V2, objTex.U[1], objTex.V[1], page);
                        AddVertex(vertices, room, tri.V3, objTex.U[2], objTex.V[2], page);
                    }
                }
            }

            // 2. OBJELER VE VARLIKLAR (Entities)

            Dictionary<short, TRModel> modelDict = [];
            if (level.Models != null)
            {
                foreach (var model in level.Models) modelDict[(short)model.ID] = model;
            }

            if (level.Entities != null)
            {
                foreach (var entity in level.Entities)
                {
                    if (_alternateRooms.Contains(entity.Room)) continue;

                    if (modelDict.TryGetValue(entity.TypeID, out TRModel model))
                    {
                        // 1. Modelin ilk animasyonunu (Varsayılan bekleme duruşu) al.
                        // Animasyonu olmayan modellerde Animation 0xFFFF olabilir; o zaman modelin kendi kare ofseti kullanılır.
                        uint frameOffset = model.Animation < level.Animations.Length
                            ? level.Animations[model.Animation].FrameOffset
                            : model.FrameOffset;

                        // TR animasyon ofsetleri byte cinsindendir. Bizim 'Frames' dizimiz short (2 byte) olduğu için 2'ye bölüyoruz!
                        int framePtr = (int)(frameOffset / 2);

                        // Kare verisi en fazla: 9 short başlık + her uzuv için 2 short dönüş. Dizinin dışına taşacaksa modeli atla.
                        if (framePtr + 9 + model.NumMeshes * 2 > level.Frames.Length) continue;

                        // İlk 9 short değeri Bounding Box (6) ve Root Offset (3) içindir. Bounding Box'ı atlıyoruz.
                        framePtr += 6;

                        // Ana gövdenin (Kalçanın) referans konumdan ne kadar kaydığı (Offset)
                        int rootOffsetX = level.Frames[framePtr++];
                        int rootOffsetY = level.Frames[framePtr++];
                        int rootOffsetZ = level.Frames[framePtr++];

                        // 2. Entity'nin dünyadaki konumu ve baktığı yön
                        float entityAngle = (entity.Angle / 32768f) * (float)Math.PI;
                        Matrix4 worldMatrix = Matrix4.CreateRotationY(-entityAngle) * Matrix4.CreateTranslation(entity.X, -entity.Y, -entity.Z);

                        // 3. Ana gövdenin (StartingMesh) Matrisi = Kendi Rotasyonu + Kendi Offseti + Dünya Matrisi
                        Matrix4 rootAnimRot = GetFrameRotation(level.Frames, ref framePtr);
                        Matrix4 currentMatrix = rootAnimRot * Matrix4.CreateTranslation(rootOffsetX, -rootOffsetY, -rootOffsetZ) * worldMatrix;

                        Stack<Matrix4> matrixStack = new Stack<Matrix4>();
                        uint meshTreeIndex = model.MeshTree / 4;

                        // Intensity1 = -1 ise nesne odanın ortam ışığını kullanır
                        float light = entity.Intensity1 >= 0
                            ? ShadeToLight(entity.Intensity1)
                            : ShadeToLight(level.Rooms[entity.Room].AmbientIntensity);

                        // Modelin tüm uzuvlarını dön
                        for (int i = 0; i < model.NumMeshes; i++)
                        {
                            var mesh = level.Meshes[model.StartingMesh + i];
                            if (mesh != null) AddMesh(vertices, mesh, currentMatrix, light);

                            // 4. Çizim bitti. Sıradaki parçaya geçerken yeni animasyon açısını al ve eklem yerini bük!
                            if (i < model.NumMeshes - 1 && level.MeshTrees != null)
                            {
                                var node = level.MeshTrees[meshTreeIndex++];

                                if ((node.Flags & 0x01) > 0) currentMatrix = matrixStack.Pop();
                                if ((node.Flags & 0x02) > 0) matrixStack.Push(currentMatrix);

                                // Sıradaki uzvun (Örn: Kolun) Frames dizisinden dönüş açısını oku
                                Matrix4 meshAnimRot = GetFrameRotation(level.Frames, ref framePtr);

                                // Yeni Matris = Animasyon Açısı + Uzvun Eklem Uzaklığı (MeshTree Offset) + Önceki Uzvun Matrisi
                                Matrix4 localOffset = Matrix4.CreateTranslation(node.OffsetX, -node.OffsetY, -node.OffsetZ);
                                currentMatrix = meshAnimRot * localOffset * currentMatrix;
                            }
                        }
                    }
                }
            }

            // 3. STATİK OBJELER (Heykeller, Meşaleler vb.)
            Dictionary<uint, TRStaticMeshModel> staticModelDict = [];
            if (level.StaticMeshModels != null)
            {
                foreach (var sm in level.StaticMeshModels)
                    staticModelDict[sm.ID] = sm;
            }

            for (int r = 0; r < level.Rooms.Length; r++)
            {
                var room = level.Rooms[r];
                if (room.StaticMeshes == null || _alternateRooms.Contains(r)) continue;

                foreach (var sm in room.StaticMeshes)
                {
                    // Objenin kalıbını bul
                    if (staticModelDict.TryGetValue(sm.ObjectID, out TRStaticMeshModel model))
                    {
                        var mesh = level.Meshes[model.Mesh];
                        if (mesh == null) continue;

                        // TR motorunda açı ushort (0-65535) değerindedir. Formül: (Açı / 32768) * Pi
                        // Konum mutlak dünya koordinatıdır; entity'lerle aynı dünya matrisi kullanılır.
                        float angleRad = (sm.Rotation / 32768f) * (float)Math.PI;
                        Matrix4 worldMatrix = Matrix4.CreateRotationY(-angleRad) * Matrix4.CreateTranslation(sm.X, -sm.Y, -sm.Z);

                        AddMesh(vertices, mesh, worldMatrix, ShadeToLight(sm.Intensity1));
                    }
                }
            }

            _vertexCount = vertices.Count / 7;

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);

            // Artık her vertex 7 float yer kaplıyor (X, Y, Z, U, V, Page, Light)
            int stride = 7 * sizeof(float);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);

            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);

        }

        private static void AddVertex(List<float> list, TR2Room room, int vIndex, float u, float v, int page)
        {
            var vert = room.Vertices[vIndex];

            // Köşe ışığı oda aydınlatmasını zaten içerir (Attributes ışık değil, bayrak alanıdır)
            float finalLight = ShadeToLight(vert.Lighting1);

            list.Add((room.Info.X + vert.X) / 1024f);
            list.Add(-vert.Y / 1024f);
            list.Add(-(room.Info.Z + vert.Z) / 1024f);
            list.Add(u);
            list.Add(v);
            list.Add(page);
            list.Add(finalLight); // 7. Parametre olarak ışığı ekledik!
        }

        // Bir modeli (entity uzvu veya statik obje) dünya matrisiyle dönüştürüp listeye ekler
        private void AddMesh(List<float> list, TRMesh mesh, Matrix4 transform, float light)
        {
            // Dokulu yüzler: UV ve sayfa ObjectTextures'tan gelir. Dörtgen iki üçgene bölünür: (1,2,3) ve (1,3,4)
            foreach (var f in mesh.TexturedRectangles)
            {
                var t = level.ObjectTextures[f.Texture & 0x7FFF];
                int page = t.TileAndFlag & 0x00FF;
                AddMeshVertex(list, mesh.Vertices[f.V1], t.U[0], t.V[0], page, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V2], t.U[1], t.V[1], page, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V3], t.U[2], t.V[2], page, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V1], t.U[0], t.V[0], page, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V3], t.U[2], t.V[2], page, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V4], t.U[3], t.V[3], page, transform, light);
            }
            foreach (var f in mesh.TexturedTriangles)
            {
                var t = level.ObjectTextures[f.Texture & 0x7FFF];
                int page = t.TileAndFlag & 0x00FF;
                AddMeshVertex(list, mesh.Vertices[f.V1], t.U[0], t.V[0], page, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V2], t.U[1], t.V[1], page, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V3], t.U[2], t.V[2], page, transform, light);
            }

            // Renkli yüzler: renk sayfasındaki ilgili hücrenin ortasından tek renk örneklenir
            foreach (var f in mesh.ColouredRectangles)
            {
                var (u, v) = PaletteUV(f.Texture >> 8);
                AddMeshVertex(list, mesh.Vertices[f.V1], u, v, _colorPage, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V2], u, v, _colorPage, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V3], u, v, _colorPage, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V1], u, v, _colorPage, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V3], u, v, _colorPage, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V4], u, v, _colorPage, transform, light);
            }
            foreach (var f in mesh.ColouredTriangles)
            {
                var (u, v) = PaletteUV(f.Texture >> 8);
                AddMeshVertex(list, mesh.Vertices[f.V1], u, v, _colorPage, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V2], u, v, _colorPage, transform, light);
                AddMeshVertex(list, mesh.Vertices[f.V3], u, v, _colorPage, transform, light);
            }
        }

        private static void AddMeshVertex(List<float> list, TRVertex v, float u, float tv, int page, Matrix4 transform, float light)
        {
            // TR'nin model koordinatlarını al (Y ve Z ekseni TR'de ters işler)
            Vector4 localPos = new(v.X, -v.Y, -v.Z, 1.0f);

            // OpenTK'nın matris gücüyle yerel koordinatı mutlak dünya koordinatına çeviriyoruz!
            Vector4 worldPos = localPos * transform;

            list.Add(worldPos.X / 1024f);
            list.Add(worldPos.Y / 1024f);
            list.Add(worldPos.Z / 1024f);

            list.Add(u);
            list.Add(tv);
            list.Add(page);

            list.Add(light);
        }

        // Renk sayfası 16x16 hücreye bölünmüştür; her hücre (16x16 piksel) bir Palette16 rengidir
        private static (float u, float v) PaletteUV(int index)
        {
            index &= 0xFF;
            return (((index % 16) * 16 + 8) / 256f, ((index / 16) * 16 + 8) / 256f);
        }

        private void UpdateCurrentRoom()
        {
            int trX = (int)(_cameraPosition.X * 1024f);
            int trY = (int)(-_cameraPosition.Y * 1024f); // TR2'de Y aşağı doğru büyür
            int trZ = (int)(-_cameraPosition.Z * 1024f);

            for (int i = 0; i < level.Rooms.Length; i++)
            {
                if (_alternateRooms.Contains(i)) continue;

                var room = level.Rooms[i];
                int minX = room.Info.X;
                int maxX = room.Info.X + (room.NumXSectors * 1024);
                int minZ = room.Info.Z;
                int maxZ = room.Info.Z + (room.NumZSectors * 1024);

                if (trX >= minX && trX < maxX && trZ >= minZ && trZ < maxZ)
                {
                    // TR2'de YTop daha küçük (negatif) bir sayıdır çünkü tavan yukarıdadır
                    if (trY >= room.Info.YTop - 2048 && trY <= room.Info.YBottom + 2048)
                    {
                        if (_currentRoom != i)
                        {
                            _currentRoom = i;
                        }
                        break;
                    }
                }
            }
        }

        // Verilen OpenGL konumunun odadaki sektörünü bulur. Oda dışındaysa false döner.
        private bool TryGetSector(int roomIndex, float glX, float glZ, out TRRoomSector sector)
        {
            sector = default;
            if (roomIndex < 0 || roomIndex >= level.Rooms.Length) return false;
            var room = level.Rooms[roomIndex];

            // Negatif değerlerde tam sayı bölmesi sıfıra yuvarlar; doğru sektör için aşağı yuvarla
            int secX = (int)MathF.Floor((glX * 1024f - room.Info.X) / 1024f);
            int secZ = (int)MathF.Floor((-glZ * 1024f - room.Info.Z) / 1024f);

            if (secX < 0 || secX >= room.NumXSectors || secZ < 0 || secZ >= room.NumZSectors) return false;

            sector = room.Sectors[(secX * room.NumZSectors) + secZ];
            return true;
        }

        // Sektör yüksekliği "click" (256 TR birimi) cinsindendir; OpenGL Y'sine çevir
        private static float ClickToGL(byte click) => -((sbyte)click * 256f) / 1024f;

        // TR2 kuralı: Floor ve Ceiling eşitse veya Floor -127 ise orası duvardır.
        private static bool IsSolid(TRRoomSector sector)
        {
            sbyte floor = (sbyte)sector.Floor;
            return floor == (sbyte)sector.Ceiling || floor <= -127;
        }

        private float GetFloorHeight(int roomIndex, float glX, float glZ)
        {
            // Açık zeminli sektörlerde (RoomBelow) asıl zemin alttaki odadadır; zinciri takip et
            for (int i = 0; i < 16; i++)
            {
                if (!TryGetSector(roomIndex, glX, glZ, out var sector)) return -9999f; // Odanın dışına çıktık
                if (sector.RoomBelow != 255)
                {
                    roomIndex = sector.RoomBelow;
                    continue;
                }
                if (IsSolid(sector)) return -9999f; // Katı Duvar
                return ClickToGL(sector.Floor);
            }
            return -9999f;
        }

        private bool IsWall(int roomIndex, float glX, float glZ)
        {
            // Oda sınırları dışı duvar sayılır
            if (!TryGetSector(roomIndex, glX, glZ, out var sector)) return true;
            return IsSolid(sector);
        }

        // Kapı sektörleri kaynak odada duvar olarak işaretlidir; geçiş bilgisi FloorData'daki portal kaydındadır.
        // Konum bir kapı sektörüne düşüyorsa komşu odanın indeksini döndürür.
        private int ResolvePortals(int roomIndex, float glX, float glZ)
        {
            for (int i = 0; i < 8; i++)
            {
                if (!TryGetSector(roomIndex, glX, glZ, out var sector)) return roomIndex;
                int next = GetPortalRoom(sector.FDIndex);
                if (next < 0 || next == roomIndex || next >= level.Rooms.Length) return roomIndex;
                roomIndex = next;
            }
            return roomIndex;
        }

        // Ayaklar zeminin altına indiyse alttaki odaya, tavanın üstüne çıktıysa üstteki odaya geç
        private int ResolveVertical(int roomIndex, float glX, float feetY, float glZ)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!TryGetSector(roomIndex, glX, glZ, out var sector)) return roomIndex;
                if (sector.RoomBelow != 255 && feetY < ClickToGL(sector.Floor)) roomIndex = sector.RoomBelow;
                else if (sector.RoomAbove != 255 && feetY > ClickToGL(sector.Ceiling)) roomIndex = sector.RoomAbove;
                else return roomIndex;
            }
            return roomIndex;
        }

        // FloorData kayıt zincirini tarar, portal (fonksiyon 1) varsa hedef odayı döndürür, yoksa -1.
        // Her kayıt başlığı: bit 0-4 fonksiyon, bit 15 "son kayıt" işareti.
        private int GetPortalRoom(ushort fdIndex)
        {
            var fd = level.FloorData;
            if (fdIndex == 0 || fd == null) return -1; // 0 = bu sektörün FloorData'sı yok

            int idx = fdIndex;
            while (idx < fd.Length)
            {
                ushort header = fd[idx++];
                int function = header & 0x1F;
                bool isLast = (header & 0x8000) != 0;

                switch (function)
                {
                    case 1: // Portal: 1 kelime = komşu oda
                        return idx < fd.Length ? fd[idx] : -1;
                    case 2: // Zemin eğimi: 1 kelime
                    case 3: // Tavan eğimi: 1 kelime
                        idx++;
                        break;
                    case 4: // Tetikleyici: 1 ayar kelimesi + bit 15 ile biten eylem listesi
                        idx++;
                        while (idx < fd.Length)
                        {
                            ushort action = fd[idx++];
                            if (((action & 0x7C00) >> 10) == 1) // Kamera eylemi ek bir kelime taşır, bitiş bayrağı onda
                            {
                                if (idx >= fd.Length || (fd[idx++] & 0x8000) != 0) break;
                            }
                            else if ((action & 0x8000) != 0) break;
                        }
                        break;
                    default: // 5 = Öldürücü zemin, 6 = Tırmanılabilir duvar: ek veri yok
                        break;
                }

                if (isLast) break;
            }
            return -1;
        }

        private static Matrix4 GetFrameRotation(short[] frames, ref int offset)
        {
            // TR motoru rotasyonları 16-bit (short) kelimeler halinde okur.
            // İlk iki bit (C000) rotasyonun eksen tipini (X, Y, Z veya Hepsi) belirler.
            ushort w1 = (ushort)frames[offset++];
            int mode = (w1 & 0xC000) >> 14;

            float rotX = 0, rotY = 0, rotZ = 0;
            float rad = (float)Math.PI * 2f / 1024f; // 10-bit değeri (0-1023) radyana çevirme çarpanı

            if (mode == 0) // 3 Eksen birden dönüyorsa (2 kelime okunur)
            {
                ushort w2 = (ushort)frames[offset++];
                int x = (w1 & 0x3FF0) >> 4;
                int y = ((w1 & 0x000F) << 6) | ((w2 & 0xFC00) >> 10);
                int z = (w2 & 0x03FF);

                rotX = x * rad;
                rotY = y * rad;
                rotZ = z * rad;
            }
            else if (mode == 1) rotX = (w1 & 0x03FF) * rad;
            else if (mode == 2) rotY = (w1 & 0x03FF) * rad;
            else if (mode == 3) rotZ = (w1 & 0x03FF) * rad;

            // TR motoru dönüşleri Y-X-Z sırasıyla birleştirir (phd_RotYXZ): köşeye önce Z, sonra X, en son Y uygulanır.
            // OpenTK satır vektörü kullandığı için çarpım sırası Z * X * Y olur.
            return Matrix4.CreateRotationZ(-rotZ) * Matrix4.CreateRotationX(rotX) * Matrix4.CreateRotationY(-rotY);
        }
    }
}
