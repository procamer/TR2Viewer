using TR2Viewer.Render;

namespace TR2Viewer
{
    class Program
    {
        static void Main(string[] args)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("Bu görüntüleyici Windows gerektirir (arayüz yazıları Windows yazı tipiyle çizilir).");
                return;
            }

            // Bölüm listesi DATA klasöründen okunur. Komut satırından bölüm verilirse doğrudan açılır
            // (TR2Viewer.exe DATA/WALL.TR2), verilmezse bölüm seçme menüsüyle başlanır.
            string? dataDirectory = ResolvePath("DATA", Directory.Exists);
            string? startLevel = args.Length > 0 ? ResolvePath(args[0], File.Exists) ?? args[0] : null;

            using var window = new TRViewer(1920, 1080, dataDirectory, startLevel);
            window.Run();
        }

        // Yolu önce çalışma klasöründe, sonra exe klasöründen yukarı doğru arar.
        // Böylece Visual Studio bin\Debug\net10.0 içinden çalıştırsa da proje klasöründeki DATA bulunur.
        private static string? ResolvePath(string relative, Func<string, bool> exists)
        {
            if (exists(relative)) return Path.GetFullPath(relative);

            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
