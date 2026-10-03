using TR2Viewer.Models;
using TR2Viewer.Render;

namespace TR2Viewer
{
    class Program
    {
        static void Main(string[] args)
        {
            // Komut satırından bölüm verilebilir: TR2Viewer.exe DATA/WALL.TR2
            string fileName = args.Length > 0 ? args[0] : "DATA/boat.TR2";
            string filePath = ResolveLevelPath(fileName);

            var level = new TR2Level(filePath);
            using var window = new TRViewer(1920, 1080, "Tomb Raider 2", level);
            window.Run();
        }

        // Dosyayı önce çalışma klasöründe, sonra exe klasöründen yukarı doğru arar.
        // Böylece Visual Studio bin\Debug\net10.0 içinden çalıştırsa da proje klasöründeki DATA bulunur.
        private static string ResolveLevelPath(string fileName)
        {
            if (File.Exists(fileName)) return fileName;

            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate)) return candidate;
            }

            return fileName; // Bulunamazsa TR2Level anlaşılır bir hata fırlatır
        }
    }
}
