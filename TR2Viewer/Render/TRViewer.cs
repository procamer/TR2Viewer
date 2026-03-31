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

        private Vector3 _cameraPosition;
        private Vector3 _cameraFront = new(0.0f, 0.0f, -1.0f);
        private Vector3 _cameraUp = Vector3.UnitY;

        private float _yaw = -90.0f;
        private float _pitch = 0.0f;
        private Vector2 _lastMousePos;
        private bool _firstMouse = true;

        private int _currentRoom = 0;
        private float _velocityY = 0f;
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
                        _cameraPosition = new Vector3(ent.X / 1024f, -ent.Y / 1024f, -ent.Z / 1024f);
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
            
            CursorState = CursorState.Grabbed;
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

            GL.BindTexture(TextureTarget.Texture2DArray, _textureArray);

            // 1. Mipmap (Uzaklaştıkça küçülen dokular) oluştur
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2DArray);



            // 2. Çizgisel (Linear) filtreleme kullan (Pikselliği giderir)
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            //GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            //GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // 3. Anisotropic Filtering (Zeminlerin uzakta net kalmasını sağlar)
            GL.GetFloat((GetPName)0x84FF, out float maxAniso); // GL_MAX_TEXTURE_MAX_ANISOTROPY_EXT
            GL.TexParameter(TextureTarget.Texture2DArray, (TextureParameterName)0x84FE, maxAniso); // GL_TEXTURE_MAX_ANISOTROPY_EXT
            
            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);

            SwapBuffers();
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
                    if (!IsWall(_currentRoom, nextPosition.X, _cameraPosition.Z)) _cameraPosition.X = nextPosition.X;
                    if (!IsWall(_currentRoom, _cameraPosition.X, nextPosition.Z)) _cameraPosition.Z = nextPosition.Z;
                }

                UpdateCurrentRoom();
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

                float floorHeight = GetFloorHeight(_currentRoom, _cameraPosition.X, _cameraPosition.Z);
                float eyeLevel = 1.0f;

                // 2. Zemine Çarpma Kontrolü
                if (_cameraPosition.Y < floorHeight + eyeLevel && floorHeight != -9999f)
                {
                    _cameraPosition.Y = floorHeight + eyeLevel;

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
            GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, 256, 256, (int)level.NumTextiles, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            for (int i = 0; i < level.NumTextiles; i++)
            {
                byte[] rgba = new byte[256 * 256 * 4];
                for (int j = 0; j < 65536; j++)
                {
                    ushort p = level.Textiles16[i].Tile[j];
                    rgba[j * 4 + 0] = (byte)(((p & 0x7C00) >> 10) * 8);
                    rgba[j * 4 + 1] = (byte)(((p & 0x03E0) >> 5) * 8);
                    rgba[j * 4 + 2] = (byte)((p & 0x001F) * 8);
                    rgba[j * 4 + 3] = (byte)((p & 0x8000) != 0 || p == 0 ? 255 : 0);
                }
                GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, i, 256, 256, 1, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            }
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }
        
        private void BuildMapGeometry()
        {
            List<float> vertices = [];

            // 1. ODALAR (Sabit Duvarlar ve Zeminler)
            foreach (var room in level.Rooms)
            {
                if (room.Vertices == null) continue;
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
                    if (modelDict.TryGetValue(entity.TypeID, out TRModel model))
                    {
                        // 1. Modelin ilk animasyonunu (Varsayılan bekleme duruşu) al
                        var anim = level.Animations[model.Animation];

                        // TR animasyon ofsetleri byte cinsindendir. Bizim 'Frames' dizimiz short (2 byte) olduğu için 2'ye bölüyoruz!
                        int framePtr = (int)(anim.FrameOffset / 2);

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

                        // Modelin tüm uzuvlarını dön
                        for (int i = 0; i < model.NumMeshes; i++)
                        {
                            var mesh = level.Meshes[model.StartingMesh + i];
                            if (mesh == null) continue;

                            float light = 1.0f; // Varsa entity.Intensity1

                            // Dörtgenleri (Quads) çiz
                            if (mesh.TexturedRectangles != null)
                            {
                                foreach (var rect in mesh.TexturedRectangles)
                                {
                                    // AddEntityVertex metodunuzdaki 'offsetY' veya 0f parametresini kendi metodunuza göre ayarlayın
                                    AddEntityVertex(vertices, mesh.Vertices[rect.V1], rect.Texture, 0, currentMatrix, light);
                                    AddEntityVertex(vertices, mesh.Vertices[rect.V2], rect.Texture, 1, currentMatrix, light);
                                    AddEntityVertex(vertices, mesh.Vertices[rect.V3], rect.Texture, 2, currentMatrix, light);

                                    AddEntityVertex(vertices, mesh.Vertices[rect.V1], rect.Texture, 0, currentMatrix, light);
                                    AddEntityVertex(vertices, mesh.Vertices[rect.V3], rect.Texture, 2, currentMatrix, light);
                                    AddEntityVertex(vertices, mesh.Vertices[rect.V4], rect.Texture, 3, currentMatrix, light);
                                }
                            }

                            // Üçgenleri (Triangles) çiz
                            if (mesh.TexturedTriangles != null)
                            {
                                foreach (var tri in mesh.TexturedTriangles)
                                {
                                    AddEntityVertex(vertices, mesh.Vertices[tri.V1], tri.Texture, 0, currentMatrix, light);
                                    AddEntityVertex(vertices, mesh.Vertices[tri.V2], tri.Texture, 1, currentMatrix, light);
                                    AddEntityVertex(vertices, mesh.Vertices[tri.V3], tri.Texture, 2, currentMatrix, light);
                                }
                            }

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

            Dictionary<uint, TRStaticMeshModel> staticModelDict = [];
            if (level.StaticMeshModels != null)
            {
                foreach (var sm in level.StaticMeshModels)
                    staticModelDict[sm.ID] = sm;
            }

            foreach (var room in level.Rooms)
            {
                if (room.StaticMeshes == null) continue;

                foreach (var sm in room.StaticMeshes)
                {
                    // Objenin kalıbını bul
                    if (staticModelDict.TryGetValue(sm.ObjectID, out TRStaticMeshModel model))
                    {
                        var mesh = level.Meshes[model.Mesh];
                        if (mesh == null) continue;

                        // Açıyı hesapla: TR motorunda açı ushort (0-65535) değerindedir.
                        // Formül: (Açı / 32768) * Pi
                        float angleRad = (sm.Rotation / 32768f) * (float)Math.PI;
                        float cosA = (float)Math.Cos(angleRad);
                        float sinA = (float)Math.Sin(angleRad);

                        // Objenin dünyadaki net koordinatı (Odanın koordinatları + objenin yerel koordinatı)
                        float worldX = sm.X;
                        float worldY = sm.Y;
                        float worldZ = sm.Z;
                        float light = 1.0f; // Varsayılan aydınlatma değeri

                        // Objeye ait Dörtgenleri (Quads) listeye ekle
                        if (mesh.TexturedRectangles != null)
                        {
                            foreach (var rect in mesh.TexturedRectangles)
                            {
                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[rect.V1], rect.Texture, 0, cosA, sinA, light);
                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[rect.V2], rect.Texture, 1, cosA, sinA, light);
                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[rect.V3], rect.Texture, 2, cosA, sinA, light);

                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[rect.V1], rect.Texture, 0, cosA, sinA, light);
                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[rect.V3], rect.Texture, 2, cosA, sinA, light);
                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[rect.V4], rect.Texture, 3, cosA, sinA, light);
                            }
                        }

                        // Objeye ait Üçgenleri (Triangles) listeye ekle
                        if (mesh.TexturedTriangles != null)
                        {
                            foreach (var tri in mesh.TexturedTriangles)
                            {
                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[tri.V1], tri.Texture, 0, cosA, sinA, light);
                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[tri.V2], tri.Texture, 1, cosA, sinA, light);
                                AddStaticVertex(vertices, worldX, worldY, worldZ, mesh.Vertices[tri.V3], tri.Texture, 2, cosA, sinA, light);
                            }
                        }
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

            // 1. Köşenin kendi ışığını hesapla (0 = Parlak, 32767 = Karanlık)
            float vertexLight = 1.0f - (vert.Attributes / 16384f);

            // 2. Odanın genel ortam ışığını hesapla (TR2 standardına göre)
            // AmbientIntensity genellikle 0 (Karanlık) ile 8192 (Parlak) arasındadır.
            float roomAmbient = room.AmbientIntensity / 8192f;

            // 3. İkisini harmanla! (Hangisi daha parlaksa onu al veya topla)
            float finalLight = vertexLight + roomAmbient;

            // Sınırları koru (Ne çok parlak, ne zifiri karanlık olsun)
            if (finalLight > 1.0f) finalLight = 1.0f;
            if (finalLight < 0.15f) finalLight = 0.15f; // Minimum %15 her zaman görünür kalsın

            list.Add((room.Info.X + vert.X) / 1024f);
            list.Add(-vert.Y / 1024f);
            list.Add(-(room.Info.Z + vert.Z) / 1024f);
            list.Add(u);
            list.Add(v);
            list.Add(page);
            list.Add(finalLight); // 7. Parametre olarak ışığı ekledik!
        }

        private void AddStaticVertex(List<float> list, float worldX, float worldY, float worldZ, TRVertex v, ushort texture, int uvIndex, float cosA, float sinA, float light)
        {
            var objTex = level.ObjectTextures[texture & 0x7FFF];
            int page = objTex.TileAndFlag & 0x00FF;

            // Statik objeleri Y ekseninde kendi merkezi etrafında döndürme matematiği
            float rotX = (v.X * cosA) + (v.Z * sinA);
            float rotZ = -(v.X * sinA) + (v.Z * cosA);

            list.Add((worldX + rotX) / 1024f);
            list.Add(-(worldY + v.Y) / 1024f); // Y koordinatları OpenTK için genellikle ters çevrilir
            list.Add(-(worldZ + rotZ) / 1024f);

            list.Add(objTex.U[uvIndex]);
            list.Add(objTex.V[uvIndex]);
            list.Add(page);

            list.Add(light);
        }
        
        private void AddEntityVertex(List<float> list, TRVertex v, ushort texture, int uvIndex, Matrix4 transform, float light)
        {
            var objTex = level.ObjectTextures[texture & 0x7FFF];
            int page = objTex.TileAndFlag & 0x00FF;

            // TR'nin model koordinatlarını al (Y ve Z ekseni TR'de ters işler)
            Vector4 localPos = new(v.X, -v.Y, -v.Z, 1.0f);

            // OpenTK'nın matris gücüyle yerel koordinatı mutlak dünya koordinatına çeviriyoruz!
            Vector4 worldPos = localPos * transform;

            list.Add(worldPos.X / 1024f);
            list.Add(worldPos.Y / 1024f);
            list.Add(worldPos.Z / 1024f);

            list.Add(objTex.U[uvIndex]);
            list.Add(objTex.V[uvIndex]);
            list.Add(page);

            list.Add(light);
        }
        
        private void UpdateCurrentRoom()
        {
            int trX = (int)(_cameraPosition.X * 1024f);
            int trY = (int)(-_cameraPosition.Y * 1024f); // TR2'de Y aşağı doğru büyür
            int trZ = (int)(-_cameraPosition.Z * 1024f);

            for (int i = 0; i < level.Rooms.Length; i++)
            {
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

        private float GetFloorHeight(int roomIndex, float glX, float glZ)
        {
            if (roomIndex < 0 || roomIndex >= level.Rooms.Length) return -9999f;
            var room = level.Rooms[roomIndex];

            int trX = (int)(glX * 1024f);
            int trZ = (int)(-glZ * 1024f);
            int secX = (trX - room.Info.X) / 1024;
            int secZ = (trZ - room.Info.Z) / 1024;

            if (secX < 0 || secX >= room.NumXSectors || secZ < 0 || secZ >= room.NumZSectors)
                return -9999f; // Odanın dışına çıktık

            var sector = room.Sectors[(secX * room.NumZSectors) + secZ];
            sbyte floor = (sbyte)sector.Floor;
            sbyte ceiling = (sbyte)sector.Ceiling;

            if (floor == ceiling || floor <= -127) return -9999f; // Katı Duvar

            return -(floor * 256f) / 1024f;
        }

        private bool IsWall(int roomIndex, float glX, float glZ)
        {
            if (roomIndex < 0 || roomIndex >= level.Rooms.Length) return true;
            var room = level.Rooms[roomIndex];

            int trX = (int)(glX * 1024f);
            int trZ = (int)(-glZ * 1024f);

            int secX = (trX - room.Info.X) / 1024;
            int secZ = (trZ - room.Info.Z) / 1024;

            // Oda sınırları dışı duvar sayılır
            if (secX < 0 || secX >= room.NumXSectors || secZ < 0 || secZ >= room.NumZSectors)
                return true;

            var sector = room.Sectors[(secX * room.NumZSectors) + secZ];
            sbyte floor = (sbyte)sector.Floor;
            sbyte ceiling = (sbyte)sector.Ceiling;

            // TR2 kuralı: Floor ve Ceiling eşitse veya Floor -127 ise orası duvardır.
            return (floor == ceiling || floor <= -127);
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

            return Matrix4.CreateRotationX(rotX) * Matrix4.CreateRotationY(-rotY) * Matrix4.CreateRotationZ(-rotZ);
        }
    }
}