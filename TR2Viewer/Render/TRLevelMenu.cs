using System.Runtime.Versioning;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace TR2Viewer.Render
{
    public enum MenuAction { None, Close, Load, Quit }

    // Oyun içi bölüm seçme menüsü: DATA klasöründeki .TR2 dosyalarını oyundaki sırayla ve adlarıyla listeler.
    public class TRLevelMenu
    {
        public record Item(string Title, string FileName, string? Path); // Path null = Çıkış

        // Tomb Raider II bölümleri, oyundaki sırayla (dosya adı, bölüm adı)
        private static readonly (string File, string Title)[] KnownLevels =
        [
            ("WALL", "The Great Wall"), ("BOAT", "Venice"), ("VENICE", "Bartoli's Hideout"), ("OPERA", "Opera House"),
            ("RIG", "Offshore Rig"), ("PLATFORM", "Diving Area"), ("UNWATER", "40 Fathoms"), ("KEEL", "Wreck of the Maria Doria"),
            ("LIVING", "Living Quarters"), ("DECK", "The Deck"), ("SKIDOO", "Tibetan Foothills"), ("MONASTRY", "Barkhang Monastery"),
            ("CATACOMB", "Catacombs of the Talion"), ("ICECAVE", "Ice Palace"), ("EMPRTOMB", "Temple of Xian"), ("FLOATING", "Floating Islands"),
            ("XIAN", "The Dragon's Lair"), ("HOUSE", "Home Sweet Home"), ("ASSAULT", "Lara's Home"),
            ("CUT1", "Ara sahne 1"), ("CUT2", "Ara sahne 2"), ("CUT3", "Ara sahne 3"), ("CUT4", "Ara sahne 4"), ("TITLE", "Başlık ekranı"),
        ];

        public List<Item> Items { get; } = [];
        public bool IsOpen { get; set; }
        public int Selected { get; set; }
        public string? Message { get; set; }   // Hata veya bilgi mesajı (ör. bölüm yüklenemedi)

        public TRLevelMenu(string? dataDirectory)
        {
            if (dataDirectory != null && Directory.Exists(dataDirectory))
            {
                var files = Directory.GetFiles(dataDirectory, "*.TR2")
                    .ToDictionary(p => Path.GetFileNameWithoutExtension(p).ToUpperInvariant(), p => p);

                foreach (var (file, title) in KnownLevels)
                {
                    if (files.Remove(file, out var path)) Items.Add(new Item(title, file, path));
                }
                foreach (var (file, path) in files.OrderBy(f => f.Key)) Items.Add(new Item(file, file, path)); // Bilinmeyenler sona
            }
            else
            {
                Message = "DATA klasörü bulunamadı";
            }
            Items.Add(new Item("Çıkış", "", null));
        }

        // Yüklü bölüm menüde seçili gelsin
        public void SelectPath(string? path)
        {
            int index = Items.FindIndex(i => i.Path != null && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Selected = index;
        }

        public MenuAction HandleInput(KeyboardState keys, bool canClose, out string? path)
        {
            path = null;
            if (keys.IsKeyPressed(Keys.Up) || keys.IsKeyPressed(Keys.W)) Selected = (Selected - 1 + Items.Count) % Items.Count;
            if (keys.IsKeyPressed(Keys.Down) || keys.IsKeyPressed(Keys.S)) Selected = (Selected + 1) % Items.Count;
            if (keys.IsKeyPressed(Keys.Home)) Selected = 0;
            if (keys.IsKeyPressed(Keys.End)) Selected = Items.Count - 1;

            if (keys.IsKeyPressed(Keys.Escape) && canClose) return MenuAction.Close;
            if (keys.IsKeyPressed(Keys.Enter) || keys.IsKeyPressed(Keys.KeyPadEnter))
            {
                var item = Items[Selected];
                if (item.Path == null) return MenuAction.Quit;
                path = item.Path;
                return MenuAction.Load;
            }
            return MenuAction.None;
        }

        private static readonly Vector4 Gold = new(0.95f, 0.78f, 0.38f, 1f);
        private static readonly Vector4 White = new(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Vector4 Dim = new(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Vector4 Green = new(0.45f, 0.85f, 0.45f, 1f);
        private static readonly Vector4 Red = new(1f, 0.45f, 0.4f, 1f);

        [SupportedOSPlatform("windows")]
        public void Draw(TRUiRenderer ui, Vector2i screen, string? currentPath, bool canClose)
        {
            float s = screen.Y / 1080f; // 1080p'ye göre ölçek
            ui.Rect(0, 0, screen.X, screen.Y, new Vector4(0f, 0f, 0f, 0.65f));

            float panelW = MathF.Min(screen.X * 0.9f, 1000f * s), panelH = screen.Y * 0.88f;
            float px = (screen.X - panelW) / 2f, py = (screen.Y - panelH) / 2f;
            ui.Rect(px, py, panelW, panelH, new Vector4(0.06f, 0.05f, 0.04f, 0.92f));
            ui.Rect(px, py, panelW, 3 * s, Gold);
            ui.Rect(px, py + panelH - 3 * s, panelW, 3 * s, Gold);

            float pad = 36f * s;
            string title = "TOMB RAIDER II";
            float titleSize = 56f * s;
            ui.Text(title, px + (panelW - ui.MeasureText(title, titleSize)) / 2f, py + pad * 0.8f, titleSize, Gold);
            string subtitle = "Bölüm Seç";
            float subSize = 30f * s;
            ui.Text(subtitle, px + (panelW - ui.MeasureText(subtitle, subSize)) / 2f, py + pad * 0.8f + titleSize * 1.15f, subSize, Dim);

            // Liste: seçili öğe görünür kalacak şekilde kaydırılır
            float textSize = 30f * s, lineH = 40f * s;
            float listTop = py + pad * 0.8f + titleSize * 1.15f + subSize * 2f;
            float footerH = 90f * s;
            int visible = Math.Max(1, (int)((py + panelH - footerH - listTop) / lineH));
            int first = Math.Clamp(Selected - visible / 2, 0, Math.Max(0, Items.Count - visible));

            for (int i = first; i < Math.Min(Items.Count, first + visible); i++)
            {
                var item = Items[i];
                float y = listTop + (i - first) * lineH;
                bool selected = i == Selected;
                bool current = item.Path != null && string.Equals(item.Path, currentPath, StringComparison.OrdinalIgnoreCase);

                if (selected) ui.Rect(px + pad * 0.5f, y - 4 * s, panelW - pad, lineH, new Vector4(Gold.X, Gold.Y, Gold.Z, 0.18f));
                if (current) ui.Text("•", px + pad * 0.6f, y, textSize, Green);

                ui.Text(item.Title, px + pad * 1.4f, y, textSize, selected ? Gold : White);
                if (item.FileName.Length > 0)
                    ui.Text(item.FileName, px + panelW - pad - ui.MeasureText(item.FileName, textSize * 0.8f), y + textSize * 0.1f, textSize * 0.8f, Dim);
            }

            // Kaydırma göstergesi
            if (first > 0) ui.Text("↑", px + panelW / 2f, listTop - lineH * 0.9f, textSize, Dim);
            if (first + visible < Items.Count) ui.Text("↓", px + panelW / 2f, listTop + visible * lineH - lineH * 0.2f, textSize, Dim);

            float footY = py + panelH - footerH * 0.6f;
            if (Message != null)
                ui.Text(Message, px + (panelW - ui.MeasureText(Message, 24f * s)) / 2f, footY - 34f * s, 24f * s, Red);

            string help = canClose ? "↑/↓ Seç   Enter Yükle   Esc Geri" : "↑/↓ Seç   Enter Yükle";
            ui.Text(help, px + (panelW - ui.MeasureText(help, 24f * s)) / 2f, footY, 24f * s, Dim);
        }

        [SupportedOSPlatform("windows")]
        public static void DrawLoading(TRUiRenderer ui, Vector2i screen, string title)
        {
            float s = screen.Y / 1080f;
            ui.Rect(0, 0, screen.X, screen.Y, new Vector4(0f, 0f, 0f, 0.85f));
            string text = $"Yükleniyor: {title}";
            float size = 40f * s;
            ui.Text(text, (screen.X - ui.MeasureText(text, size)) / 2f, screen.Y / 2f - size / 2f, size, Gold);
        }
    }
}
