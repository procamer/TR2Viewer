using TR2Viewer.Models;
using TR2Viewer.Render;

namespace TR2Viewer
{
    class Program
    {
        static void Main()
        {
            string filePath = "levels/rig.TR2";
            var level = new TR2Level(filePath);
            using var window = new TRViewer(1920, 1080, "Tomb Raider 2", level);
            window.Run();
        }
    }
}
