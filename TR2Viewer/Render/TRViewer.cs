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
        private int _vertexCount;          // Odalar + statik objeler (tamponun başında)
        private (int First, int Count)[] _meshRanges = []; // Her mesh'in tampondaki köşe aralığı
        private int _textureArray;
        private int _colorPage;            // Renkli yüzler için Palette16'dan üretilen ek doku katmanı

        private Vector3 _cameraPosition;
        private Vector3 _cameraFront = new(0.0f, 0.0f, -1.0f);
        private Vector3 _cameraUp = Vector3.UnitY;

        // Serbest kamera açıları (derece)
        private float _yaw = -90.0f;
        private float _pitch = 0.0f;
        private Vector2 _lastMousePos;
        private bool _firstMouse = true;

        // Flipmap: bölümde bir olay olunca normal odanın yerini alan "alternatif" odalar.
        // Varsayılan durumda çizilmezler, yoksa normal odayla üst üste biner.
        private readonly HashSet<int> _alternateRooms = [];

        // Animasyon
        private readonly TRAnimator _animator = new(level);
        private readonly List<TRAnimatedEntity> _entities = [];
        private bool _animationsPaused = false;
        private bool _pKeyPressed = false;

        // Lara ve takip kamerası
        private readonly TRCollision _collision = new(level);
        private TRLaraController? _lara;
        private bool _freeCamera = false;   // N: serbest uçuş kamerası / Lara'yı takip eden kamera
        private bool _nKeyPressed = false;
        private float _orbitYaw = 0f;       // Kameranın Lara'nın arkasından sapması (derece, fareyle)
        private float _orbitPitch = 15f;    // Kameranın yukarıdan bakış açısı (derece)
        private const float CameraDistance = 1536f / 1024f; // Oyundaki gibi ~1.5 sektör geride
        private const float CameraTargetHeight = 512f / 1024f; // Bakılan nokta: Lara'nın bel hizası (tüm boyu kadraja girer)

        // Shader uniform konumları (shader derlendikten sonra bir kez alınır)
        private int _viewLoc, _projLoc, _modelLoc, _lightLoc;

        // Mesh köşeleri tamponda 1/1024 ölçeğinde durur; animasyon matrisleri ise TR birimiyle çalışır.
        // Uzuv matrisini tampondaki ölçeğe uyarlamak için: ToTR * uzuv * FromTR
        private static readonly Matrix4 ToTR = Matrix4.CreateScale(1024f);
        private static readonly Matrix4 FromTR = Matrix4.CreateScale(1f / 1024f);

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
            SetupEntities();
            TRShader.CompileShaders();

            int program = TRShaderHelpers._shaderProgram;
            _viewLoc = GL.GetUniformLocation(program, "view");
            _projLoc = GL.GetUniformLocation(program, "projection");
            _modelLoc = GL.GetUniformLocation(program, "model");
            _lightLoc = GL.GetUniformLocation(program, "lightScale");

            if (_lara != null)
            {
                UpdateFollowCamera(0f, snap: true);
            }
            else if (level.Rooms.Length > 0)
            {
                // Lara'sız bölüm (ör. ara sahneler): serbest kamerayla ilk odadan başla
                _freeCamera = true;
                _cameraPosition = new Vector3((level.Rooms[0].Info.X / 1024f) + 3f, (-level.Rooms[0].Info.YTop / 1024f) - 3f, (-level.Rooms[0].Info.Z / 1024f) - 3f);
            }

            Console.WriteLine($"Yüklendi: {level.Rooms.Length} oda, {_vertexCount / 3} üçgen, {_entities.Count} varlık, kamera {_cameraPosition}");

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

            GL.UniformMatrix4(_viewLoc, false, ref view);
            GL.UniformMatrix4(_projLoc, false, ref projection);

            // Doku filtreleri LoadTextures içinde bir kez ayarlanır; her karede tekrar etmeye gerek yok
            GL.BindTexture(TextureTarget.Texture2DArray, _textureArray);
            GL.BindVertexArray(_vao);

            // 1. Odalar ve statik objeler: zaten dünya koordinatında
            Matrix4 identity = Matrix4.Identity;
            GL.UniformMatrix4(_modelLoc, false, ref identity);
            GL.Uniform1(_lightLoc, 1f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);

            // 2. Varlıklar: her uzuv kendi animasyon matrisiyle çizilir
            foreach (var entity in _entities)
            {
                GL.Uniform1(_lightLoc, entity.Light);
                for (int i = 0; i < entity.MeshMatrices.Length; i++)
                {
                    int meshIndex = entity.Model.StartingMesh + i;
                    if (meshIndex >= _meshRanges.Length) break;

                    var (first, count) = _meshRanges[meshIndex];
                    if (count == 0) continue;

                    Matrix4 model = ToTR * entity.MeshMatrices[i] * FromTR;
                    GL.UniformMatrix4(_modelLoc, false, ref model);
                    GL.DrawArrays(PrimitiveType.Triangles, first, count);
                }
            }


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
            float dt = (float)e.Time;

            if (input.IsKeyDown(Keys.Escape)) Close();

            // N: Lara'yı takip eden kamera ile serbest uçuş kamerası arasında geçiş
            bool isNPressed = input.IsKeyDown(Keys.N);
            if (isNPressed && !_nKeyPressed && _lara != null)
            {
                _freeCamera = !_freeCamera;
                if (_freeCamera)
                {
                    // Serbest kameraya geçerken bakış yönünü koru
                    _yaw = MathHelper.RadiansToDegrees(MathF.Atan2(_cameraFront.Z, _cameraFront.X));
                    _pitch = MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(_cameraFront.Y, -1f, 1f)));
                }
                Console.WriteLine("Kamera: " + (_freeCamera ? "SERBEST (uçuş)" : "LARA (takip)"));
            }
            _nKeyPressed = isNPressed;

            // P: Lara dışındaki varlıkların animasyonlarını durdur/devam ettir
            bool isPPressed = input.IsKeyDown(Keys.P);
            if (isPPressed && !_pKeyPressed)
            {
                _animationsPaused = !_animationsPaused;
                Console.WriteLine("Animasyonlar: " + (_animationsPaused ? "DURDURULDU" : "OYNUYOR"));
            }
            _pKeyPressed = isPPressed;

            if (!_animationsPaused)
            {
                foreach (var entity in _entities)
                {
                    if (entity != _lara?.Entity) _animator.Update(entity, dt);
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
            const float sensitivity = 0.1f;

            if (_freeCamera || _lara == null)
            {
                _yaw += deltaX * sensitivity;
                _pitch = Math.Clamp(_pitch - deltaY * sensitivity, -89f, 89f);

                _cameraFront.X = MathF.Cos(MathHelper.DegreesToRadians(_pitch)) * MathF.Cos(MathHelper.DegreesToRadians(_yaw));
                _cameraFront.Y = MathF.Sin(MathHelper.DegreesToRadians(_pitch));
                _cameraFront.Z = MathF.Cos(MathHelper.DegreesToRadians(_pitch)) * MathF.Sin(MathHelper.DegreesToRadians(_yaw));
                _cameraFront = Vector3.Normalize(_cameraFront);

                UpdateFreeCamera(input, dt);
                _lara?.Update(dt, default); // Lara olduğu yerde bekler (nefes alma animasyonu sürer)
            }
            else
            {
                // Fare kamerayı Lara'nın etrafında döndürür
                _orbitYaw += deltaX * sensitivity;
                _orbitPitch = Math.Clamp(_orbitPitch + deltaY * sensitivity, -10f, 60f);

                var laraInput = new LaraInput
                {
                    Forward = input.IsKeyDown(Keys.W) || input.IsKeyDown(Keys.Up),
                    Back = input.IsKeyDown(Keys.S) || input.IsKeyDown(Keys.Down),
                    Left = input.IsKeyDown(Keys.A) || input.IsKeyDown(Keys.Left),
                    Right = input.IsKeyDown(Keys.D) || input.IsKeyDown(Keys.Right),
                    Walk = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift),
                    Jump = input.IsKeyDown(Keys.Space),
                    Action = input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl)
                };
                _lara.Update(dt, laraInput);

                // Lara hareket ederken kamera yavaşça onun arkasına döner (oyundaki gibi)
                if (laraInput.Forward || laraInput.Back) _orbitYaw *= MathF.Exp(-dt * 2f);

                UpdateFollowCamera(dt, snap: false);
            }
        }

        // Serbest uçuş: WASD ileri/geri/yan, Space yukarı, Sol Shift aşağı; engel tanımaz
        private void UpdateFreeCamera(KeyboardState input, float dt)
        {
            Vector3 right = Vector3.Normalize(Vector3.Cross(_cameraFront, _cameraUp));
            Vector3 moveDir = Vector3.Zero;

            if (input.IsKeyDown(Keys.W)) moveDir += _cameraFront;
            if (input.IsKeyDown(Keys.S)) moveDir -= _cameraFront;
            if (input.IsKeyDown(Keys.A)) moveDir -= right;
            if (input.IsKeyDown(Keys.D)) moveDir += right;
            if (input.IsKeyDown(Keys.Space)) moveDir += _cameraUp;
            if (input.IsKeyDown(Keys.LeftShift)) moveDir -= _cameraUp;

            if (moveDir != Vector3.Zero) _cameraPosition += Vector3.Normalize(moveDir) * 5.0f * dt;
        }

        // Kamerayı Lara'nın arkasına ve biraz yukarısına yerleştirir. Duvara girmemesi için Lara'dan
        // istenen noktaya doğru adım adım ilerler ve açık alanda kalan son noktada durur.
        private void UpdateFollowCamera(float dt, bool snap)
        {
            var lara = _lara!;
            Vector3 target = lara.RenderPositionGL + new Vector3(0f, CameraTargetHeight, 0f);

            // TR açısı 0 = OpenGL'de -Z yönü; Lara'nın ileri yönü (sin, 0, -cos), kamera bunun tersinde durur
            float yaw = lara.RenderAngle + MathHelper.DegreesToRadians(_orbitYaw);
            float pitch = MathHelper.DegreesToRadians(_orbitPitch);
            Vector3 back = new(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
            Vector3 desired = target + back * CameraDistance;

            int room = lara.Room;
            Vector3 safe = target;
            const int steps = 24;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(target, desired, i / (float)steps);
                room = _collision.ResolvePortals(room, p.X, p.Z);
                room = _collision.ResolveVertical(room, p.X, p.Y, p.Z);
                if (!_collision.IsOpen(room, p.X, p.Y, p.Z, 0.1f)) break;
                safe = p;
            }

            _cameraPosition = snap ? safe : Vector3.Lerp(_cameraPosition, safe, 1f - MathF.Exp(-dt * 12f));

            Vector3 look = target - _cameraPosition;
            if (look.LengthSquared > 0.0001f) _cameraFront = Vector3.Normalize(look);
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

            // 2. STATİK OBJELER (Heykeller, Meşaleler vb.)
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

            // 3. MODEL PARÇALARI (Entity uzuvları): her mesh yerel koordinatta tampona bir kez eklenir,
            // her karede o anki animasyon matrisiyle çizilir (bkz. OnRenderFrame)
            _meshRanges = new (int, int)[level.Meshes.Length];
            for (int m = 0; m < level.Meshes.Length; m++)
            {
                var mesh = level.Meshes[m];
                if (mesh == null) continue;

                int first = vertices.Count / 7;
                AddMesh(vertices, mesh, Matrix4.Identity, 1f);
                _meshRanges[m] = (first, vertices.Count / 7 - first);
            }

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

        // Haritaya yerleştirilmiş, modeli olan varlıkları (Lara, düşmanlar, kapılar...) animasyona hazırla
        private void SetupEntities()
        {
            Dictionary<short, TRModel> modelDict = [];
            foreach (var model in level.Models) modelDict[(short)model.ID] = model;

            foreach (var entity in level.Entities)
            {
                if (_alternateRooms.Contains(entity.Room)) continue;
                if (!modelDict.TryGetValue(entity.TypeID, out TRModel model)) continue; // Sprite varlıklar modelsizdir

                // Entity'nin dünyadaki konumu ve baktığı yön
                float entityAngle = (entity.Angle / 32768f) * (float)Math.PI;

                var animated = new TRAnimatedEntity
                {
                    Model = model,
                    World = Matrix4.CreateRotationY(-entityAngle) * Matrix4.CreateTranslation(entity.X, -entity.Y, -entity.Z),
                    // Intensity1 = -1 ise nesne odanın ortam ışığını kullanır
                    Light = entity.Intensity1 >= 0
                        ? ShadeToLight(entity.Intensity1)
                        : ShadeToLight(level.Rooms[entity.Room].AmbientIntensity)
                };
                animated.TypeID = entity.TypeID;
                if (entity.TypeID == 0 && _lara == null)
                {
                    // Lara'nın model varsayılanı 0 = koşma animasyonudur; oyun onu 11 = "dur" ile başlatır.
                    // Animasyonunu ve konumunu bundan sonra kontrolcü yönetir.
                    _animator.Start(animated, TRLaraController.StandAnimation);
                    _lara = new TRLaraController(level, _animator, _collision, animated, entity);
                }
                else
                {
                    _animator.Start(animated);
                }
                _entities.Add(animated);
            }
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

    }
}
