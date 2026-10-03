using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace TR2Viewer.Render
{
    // Ekran üstü 2D arayüz (menü, yazılar) çizer. Yazılar açılışta Windows yazı tipinden üretilen
    // bir karakter dokusundan (atlas) çizilir; tüm karakterler eşit genişlikte (Consolas).
    [SupportedOSPlatform("windows")]
    public sealed class TRUiRenderer : IDisposable
    {
        private const string Characters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "ÇçĞğİıÖöŞşÜü↑↓←→•";
        private const float AtlasFontSize = 48f; // Atlas bu boyutta üretilir, çizerken ölçeklenir
        private const int Columns = 16;

        private readonly Dictionary<char, Vector4> _glyphUV = []; // (u0, v0, u1, v1)
        private Vector2 _whiteUV;        // Dikdörtgenler için atlastaki düz beyaz hücre
        private float _cellWidth, _cellHeight, _advance;

        private int _program, _vao, _vbo, _texture, _projLoc;
        private readonly List<float> _vertices = [];
        private Vector2i _screen;

        public TRUiRenderer()
        {
            BuildAtlas();
            CompileShader();

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            int stride = 8 * sizeof(float); // x, y, u, v, r, g, b, a
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
            GL.EnableVertexAttribArray(2);
        }

        private void BuildAtlas()
        {
            using var font = new Font("Consolas", AtlasFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using (var probe = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(probe))
            {
                SizeF size = g.MeasureString("W", font, PointF.Empty, StringFormat.GenericTypographic);
                _advance = size.Width;
                _cellWidth = MathF.Ceiling(size.Width) + 4;
                _cellHeight = MathF.Ceiling(font.GetHeight()) + 4;
            }

            int cells = Characters.Length + 1; // +1: düz beyaz hücre
            int rows = (cells + Columns - 1) / Columns;
            int width = (int)(_cellWidth * Columns), height = (int)(_cellHeight * rows);

            using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < cells; i++)
                {
                    float x = (i % Columns) * _cellWidth, y = (i / Columns) * _cellHeight;
                    if (i < Characters.Length)
                    {
                        g.DrawString(Characters[i].ToString(), font, Brushes.White, x + 2, y + 2, StringFormat.GenericTypographic);
                        _glyphUV[Characters[i]] = new Vector4(x / width, y / height, (x + _cellWidth) / width, (y + _cellHeight) / height);
                    }
                    else
                    {
                        g.FillRectangle(Brushes.White, x, y, _cellWidth, _cellHeight);
                        _whiteUV = new Vector2((x + _cellWidth / 2) / width, (y + _cellHeight / 2) / height);
                    }
                }
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            _texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            bitmap.UnlockBits(data);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        private void CompileShader()
        {
            // Not: GLSL kaynağında sadece ASCII karakter ve İngilizce yorum kullanılır.
            const string vertex = @"
                #version 330 core
                layout(location = 0) in vec2 aPos;   // Screen position in pixels
                layout(location = 1) in vec2 aUv;
                layout(location = 2) in vec4 aColor;
                uniform mat4 projection;             // Pixel space to clip space
                out vec2 Uv;
                out vec4 Color;
                void main()
                {
                    gl_Position = projection * vec4(aPos, 0.0, 1.0);
                    Uv = aUv;
                    Color = aColor;
                }";

            const string fragment = @"
                #version 330 core
                in vec2 Uv;
                in vec4 Color;
                out vec4 FragColor;
                uniform sampler2D atlas;             // White glyphs, coverage in alpha
                void main()
                {
                    FragColor = vec4(Color.rgb, Color.a * texture(atlas, Uv).a);
                }";

            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertex);
            GL.CompileShader(vs);
            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragment);
            GL.CompileShader(fs);

            _program = GL.CreateProgram();
            GL.AttachShader(_program, vs);
            GL.AttachShader(_program, fs);
            GL.LinkProgram(_program);
            GL.GetProgram(_program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) Console.WriteLine("[KRİTİK HATA] Arayüz shader'ı bağlanamadı: " + GL.GetProgramInfoLog(_program));

            GL.DetachShader(_program, vs);
            GL.DetachShader(_program, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            _projLoc = GL.GetUniformLocation(_program, "projection");
        }

        // ---------- Çizim komutları (piksel koordinatı, sol üst köşe = 0,0) ----------

        public void Begin(Vector2i screen)
        {
            _screen = screen;
            _vertices.Clear();
        }

        public void Rect(float x, float y, float w, float h, Vector4 color)
        {
            AddQuad(x, y, x + w, y + h, _whiteUV.X, _whiteUV.Y, _whiteUV.X, _whiteUV.Y, color);
        }

        // Yazının piksel cinsinden yüksekliği "size" olur
        public void Text(string text, float x, float y, float size, Vector4 color)
        {
            float scale = size / AtlasFontSize;
            float cw = _cellWidth * scale, ch = _cellHeight * scale, step = _advance * scale;
            foreach (char c in text)
            {
                if (_glyphUV.TryGetValue(c, out var uv) && c != ' ')
                    AddQuad(x - 2 * scale, y - 2 * scale, x - 2 * scale + cw, y - 2 * scale + ch, uv.X, uv.Y, uv.Z, uv.W, color);
                x += step;
            }
        }

        public float MeasureText(string text, float size) => text.Length * _advance * size / AtlasFontSize;

        private void AddQuad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, Vector4 c)
        {
            void V(float x, float y, float u, float v)
            {
                _vertices.Add(x); _vertices.Add(y); _vertices.Add(u); _vertices.Add(v);
                _vertices.Add(c.X); _vertices.Add(c.Y); _vertices.Add(c.Z); _vertices.Add(c.W);
            }
            V(x0, y0, u0, v0); V(x1, y0, u1, v0); V(x1, y1, u1, v1);
            V(x0, y0, u0, v0); V(x1, y1, u1, v1); V(x0, y1, u0, v1);
        }

        public void End()
        {
            if (_vertices.Count == 0) return;

            // 3D sahnenin üstüne: derinlik testi yok, normal saydamlık karışımı
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.SampleAlphaToCoverage);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.UseProgram(_program);
            Matrix4 projection = Matrix4.CreateOrthographicOffCenter(0, _screen.X, _screen.Y, 0, -1, 1);
            GL.UniformMatrix4(_projLoc, false, ref projection);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _texture);

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Count * sizeof(float), _vertices.ToArray(), BufferUsageHint.StreamDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertices.Count / 8);

            GL.Enable(EnableCap.SampleAlphaToCoverage);
            GL.Enable(EnableCap.DepthTest);
        }

        public void Dispose()
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            GL.DeleteTexture(_texture);
            GL.DeleteProgram(_program);
        }
    }
}
