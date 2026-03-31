using OpenTK.Graphics.OpenGL4;

namespace TR2Viewer.Render
{
    internal static class TRShaderHelpers
    {
        public static int _shaderProgram;
    }

    public class TRShader
    {
        public static void CompileShaders()
        {
            string vShader = @"
                # version 330 core
                layout(location = 0) in vec3 aPos;
                layout(location = 1) in vec3 aTex; // X=U, Y=V, Z=Page
                layout(location = 2) in float aLight;

                out vec3 TexCoord;
                out float LightValue;

                uniform mat4 view;
                uniform mat4 projection;

                void main()
                {
                    gl_Position = projection * view * vec4(aPos, 1.0);
                    TexCoord = aTex;
                    LightValue = aLight;
                }";

            string fShader = @"
                #version 330 core
                out vec4 FragColor;

                in vec3 TexCoord;
                in float LightValue;

                uniform sampler2DArray textureArray;

                void main() {
                    vec4 texColor = texture(textureArray, TexCoord);
                    if(texColor.a < 0.2 || (texColor.r < 0.02 && texColor.g < 0.02 && texColor.b < 0.02)) 
                    {
                        discard;
                    }
                    vec3 finalColor = texColor.rgb * LightValue;
                    finalColor = pow(finalColor, vec3(1.0 / 1.2)); 
                    FragColor = vec4(finalColor, texColor.a);
                }";






            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vShader); GL.CompileShader(vs);
            CheckShaderError(vs, "Vertex Shader");

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fShader); GL.CompileShader(fs);
            CheckShaderError(fs, "Fragment Shader");
            TRShaderHelpers.
                        _shaderProgram = GL.CreateProgram();
            GL.AttachShader(TRShaderHelpers._shaderProgram, vs); GL.AttachShader(TRShaderHelpers._shaderProgram, fs);
            GL.LinkProgram(TRShaderHelpers._shaderProgram);
        }

        private static void CheckShaderError(int shaderId, string shaderName)
        {
            GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                Console.WriteLine($"\n[KRİTİK HATA] {shaderName} derlenemedi!");
                Console.WriteLine(GL.GetShaderInfoLog(shaderId));
            }
        }
    }
}
