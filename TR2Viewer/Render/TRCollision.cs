using TR2Viewer.Models;

namespace TR2Viewer.Render
{
    // Sektör tabanlı çarpışma sorguları (OpenGL koordinatlarıyla: 1 birim = 1024 TR birimi, Y yukarı, Z ters).
    // Lara'nın hareketi ve takip kamerası tarafından kullanılır.
    public class TRCollision(TR2Level level)
    {
        // Verilen OpenGL konumunun odadaki sektörünü bulur. Oda dışındaysa false döner.
        public bool TryGetSector(int roomIndex, float glX, float glZ, out TRRoomSector sector)
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
        public static float ClickToGL(byte click) => -((sbyte)click * 256f) / 1024f;

        // TR2 kuralı: Floor ve Ceiling eşitse veya Floor -127 ise orası duvardır.
        public static bool IsSolid(TRRoomSector sector)
        {
            sbyte floor = (sbyte)sector.Floor;
            return floor == (sbyte)sector.Ceiling || floor <= -127;
        }

        public float GetFloorHeight(int roomIndex, float glX, float glZ)
        {
            if (!TryGetFloorSector(roomIndex, glX, glZ, out var sector)) return -9999f;
            return -FloorHeightTR(sector, glX, glZ) / 1024f;
        }

        // Oda bayraklarının 0. biti: su odası
        public bool IsWaterRoom(int roomIndex) =>
            roomIndex >= 0 && roomIndex < level.Rooms.Length && (level.Rooms[roomIndex].Flags & 1) != 0;

        // Noktanın üstündeki su yüzeyi (TR Y). Su sütununun en üst su odasının tavanıdır; üstünde
        // havalı bir oda yoksa (kapalı su) yüzey yoktur. Hava odasından sorulursa altındaki suya bakılır.
        public bool TryGetWaterSurface(int roomIndex, float glX, float glZ, out float surfaceY)
        {
            surfaceY = 0f;
            for (int i = 0; i < 16; i++)
            {
                if (!TryGetSector(roomIndex, glX, glZ, out var sector)) return false;

                if (!IsWaterRoom(roomIndex))
                {
                    // Hava odası: zemini açık ve altı su ise yüzey bu odanın zeminidir
                    if (sector.RoomBelow == 255 || !IsWaterRoom(sector.RoomBelow)) return false;
                    surfaceY = (sbyte)sector.Floor * 256f;
                    return true;
                }

                if (sector.RoomAbove == 255) return false; // Kapalı su: yüzey yok
                if (IsWaterRoom(sector.RoomAbove))
                {
                    roomIndex = sector.RoomAbove;
                    continue;
                }
                surfaceY = (sbyte)sector.Ceiling * 256f;
                return true;
            }
            return false;
        }

        // Noktanın asıl zeminini taşıyan sektörü bulur. Açık zeminli sektörlerde (RoomBelow) asıl zemin
        // alttaki odadadır; zinciri takip eder. Oda dışı veya katı duvarsa false.
        public bool TryGetFloorSector(int roomIndex, float glX, float glZ, out TRRoomSector sector)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!TryGetSector(roomIndex, glX, glZ, out sector)) return false; // Odanın dışına çıktık
                if (sector.RoomBelow != 255)
                {
                    roomIndex = sector.RoomBelow;
                    continue;
                }
                return !IsSolid(sector);
            }
            sector = default;
            return false;
        }

        // Zemin eğimi (FloorData fonksiyon 2): sektör boyunca Z ve X yönündeki eğim, "click" cinsinden.
        // FloorHeightTR formülüne göre yükseklik (aşağı pozitif) Z'de -slopeZ/4, X'te -slopeX/4 oranında değişir.
        public bool TryGetFloorSlope(int roomIndex, float glX, float glZ, out int slopeZ, out int slopeX)
        {
            slopeZ = slopeX = 0;
            if (!TryGetFloorSector(roomIndex, glX, glZ, out var sector)) return false;
            if (!TryGetFloorDataWord(sector.FDIndex, 2, out ushort slope)) return false;
            slopeZ = (sbyte)(slope >> 8);
            slopeX = (sbyte)(slope & 0xFF);
            return true;
        }

        // Sektörün (x, z) noktasındaki zemin yüksekliği (TR birimi, aşağı pozitif).
        // Eğimli sektörlerde FloorData'daki eğim kaydı (fonksiyon 2) TR motorunun GetHeight formülüyle uygulanır:
        // üst byte Z yönündeki, alt byte X yönündeki eğimdir (sektör boyunca "click" cinsinden).
        public float FloorHeightTR(TRRoomSector sector, float glX, float glZ)
        {
            float height = (sbyte)sector.Floor * 256f;
            if (!TryGetFloorDataWord(sector.FDIndex, 2, out ushort slope)) return height;

            int xoff = (sbyte)(slope >> 8);
            int yoff = (sbyte)(slope & 0xFF);
            int x = (int)MathF.Floor(glX * 1024f) & 1023; // Sektör içindeki konum (0-1023)
            int z = (int)MathF.Floor(-glZ * 1024f) & 1023;

            height += xoff < 0 ? -(xoff * z) / 4f : (xoff * (1023 - z)) / 4f;
            height += yoff < 0 ? -(yoff * x) / 4f : (yoff * (1023 - x)) / 4f;
            return height;
        }

        public float GetCeilingHeight(int roomIndex, float glX, float glZ)
        {
            // Açık tavanlı sektörlerde (RoomAbove) asıl tavan üstteki odadadır; zinciri takip et
            for (int i = 0; i < 16; i++)
            {
                if (!TryGetSector(roomIndex, glX, glZ, out var sector)) return 9999f;
                if (sector.RoomAbove != 255)
                {
                    roomIndex = sector.RoomAbove;
                    continue;
                }
                if (IsSolid(sector)) return 9999f;
                return ClickToGL(sector.Ceiling);
            }
            return 9999f;
        }

        // Nokta, verilen odanın açık alanında mı? (duvar değil, zeminin üstünde ve tavanın altında, margin kadar payla)
        public bool IsOpen(int roomIndex, float glX, float glY, float glZ, float margin)
        {
            if (!TryGetSector(roomIndex, glX, glZ, out var sector) || IsSolid(sector)) return false;

            float floor = GetFloorHeight(roomIndex, glX, glZ);
            float ceiling = GetCeilingHeight(roomIndex, glX, glZ);
            return glY > floor + margin && glY < ceiling - margin;
        }

        public bool IsWall(int roomIndex, float glX, float glZ)
        {
            // Oda sınırları dışı duvar sayılır
            if (!TryGetSector(roomIndex, glX, glZ, out var sector)) return true;
            return IsSolid(sector);
        }

        // Kapı sektörleri kaynak odada duvar olarak işaretlidir; geçiş bilgisi FloorData'daki portal kaydındadır.
        // Konum bir kapı sektörüne düşüyorsa komşu odanın indeksini döndürür.
        public int ResolvePortals(int roomIndex, float glX, float glZ)
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
        public int ResolveVertical(int roomIndex, float glX, float feetY, float glZ)
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

        // Portal (fonksiyon 1) varsa hedef odayı döndürür, yoksa -1
        public int GetPortalRoom(ushort fdIndex) => TryGetFloorDataWord(fdIndex, 1, out ushort room) ? room : -1;

        // FloorData kayıt zincirini tarar; istenen fonksiyonun (1 = portal, 2 = zemin eğimi, 3 = tavan eğimi)
        // veri kelimesini döndürür. Her kayıt başlığı: bit 0-4 fonksiyon, bit 15 "son kayıt" işareti.
        public bool TryGetFloorDataWord(ushort fdIndex, int wanted, out ushort data)
        {
            data = 0;
            var fd = level.FloorData;
            if (fdIndex == 0 || fd == null) return false; // 0 = bu sektörün FloorData'sı yok

            int idx = fdIndex;
            while (idx < fd.Length)
            {
                ushort header = fd[idx++];
                int function = header & 0x1F;
                bool isLast = (header & 0x8000) != 0;

                if (function == wanted && (function == 1 || function == 2 || function == 3))
                {
                    if (idx >= fd.Length) return false;
                    data = fd[idx];
                    return true;
                }

                switch (function)
                {
                    case 1: // Portal: 1 kelime = komşu oda
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
            return false;
        }
    }
}
