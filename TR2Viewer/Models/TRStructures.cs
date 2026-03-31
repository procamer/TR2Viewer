namespace TR2Viewer.Models
{
    public struct TRColor
    {
        public byte R, G, B;
    }

    public struct TRColor4
    {
        public byte R, G, B, A; // 16-bit palette genellikle ARGB5551 kullanılır, ancak biz bunu parse ederken 32-bit'e çevirebiliriz.
    }

    public struct TRTextile8
    {
        public byte[] Tile; // 256x256 byte = 65536 byte
    }

    public struct TRTextile16
    {
        public ushort[] Tile; // 256x256 ushort = 131072 byte
    }

    // 3D Model Noktası (Odalardaki vertexlerden farklıdır, aydınlatma içermez)
    public struct TRVertex
    { 
        public short X, Y, Z;
    }
    
    //public struct TRRoomData
    //{
    //    public TRRoomVertex[] Vertices;
    //    public TRFace4[] Rectangles;
    //    public TRFace3[] Triangles;
    //    public TRRoomSprite[] Sprites;
    //}

    // Mesh Data için
    public class TRMesh
    {
        // Merkezi koordinat, çap, köşeler, normaller, ışıklar, yüzeyler...
        public TRVertex[] Vertices;
        public TRFace4[] TexturedRectangles;
        public TRFace3[] TexturedTriangles;
    }

    public struct TRFloorData
    {
        public ushort[] Data; // Çarpışma, tetikleyiciler vb.
    }
    
    public struct TRAnimation
    {
        public uint FrameOffset;    // Frames dizisindeki başlangıç noktası (Çok önemli!)
        public byte FrameRate;      // Oyunun kaç karesinde bir bu animasyonun oynatılacağı
        public byte FrameSize;      // Her bir animasyon karesinin boyutu (Veri atlamak için)
        public ushort StateID;      // Durum (Koşma, durma, zıplama vb.)
        public int Speed;           // İleriye doğru hareket hızı
        public int Accel;           // İvme
        public ushort FrameStart;   // Animasyonun başladığı kare numarası
        public ushort FrameEnd;     // Animasyonun bittiği kare numarası
        public ushort NextAnimation;// Bu bitince sıradaki animasyon hangisi?
        public ushort NextFrame;    // Sonraki animasyonun hangi karesinden başlasın?
        public ushort NumStateChanges;
        public ushort StateChangeOffset;
        public ushort NumAnimCommands;
        public ushort AnimCommandOffset;
    }

    // Oyun içindeki Modellerin Şablonu (18 byte)
    public struct TRModel
    {
        public uint ID;             // Obje Tipi (Örn: 0 = Lara, 59 = Tuzak Kapak)
        public ushort NumMeshes;    // Modelin kaç parçadan oluştuğu
        public ushort StartingMesh; // Mesh (3D Veri) dizisindeki başlangıç indeksi
        public uint MeshTree;       // İskelet sistemi (Animasyon için)
        public uint FrameOffset;    // Animasyon karelerinin yeri
        public ushort Animation;    // Başlangıç animasyonu
    }

    public struct TRStaticMeshModel
    {
        public uint ID;
        public ushort Mesh;           // MeshData dizisindeki hangi mesh'i kullanıyor?
        public short[] VisibilityBox; // 6 adet short
        public short[] CollisionBox;  // 6 adet short
        public ushort Flags;
    }
    //public struct TRStaticMeshModel
    //{
    //    public uint ID;
    //    public ushort Mesh;
    //    public ushort VisibilityBoxXMin;
    //    public ushort VisibilityBoxXMax;
    //    public ushort VisibilityBoxYMin;
    //    public ushort VisibilityBoxYMax;
    //    public ushort VisibilityBoxZMin;
    //    public ushort VisibilityBoxZMax;
    //    public ushort CollisionBoxXMin;
    //    public ushort CollisionBoxXMax;
    //    public ushort CollisionBoxYMin;
    //    public ushort CollisionBoxYMax;
    //    public ushort CollisionBoxZMin;
    //    public ushort CollisionBoxZMax;
    //    public ushort Flags; // 32 byte toplam
    //}

    // UV ve Kaplama Sayfası yapısı
    public struct TRObjectTexture
    {
        public ushort Attribute;
        public ushort TileAndFlag;
        public float[] U; // 4 Köşe için U (Yatay) koordinatı
        public float[] V; // 4 Köşe için V (Dikey) koordinatı
    }

    public struct TRSpriteSequence
    {
        public int SpriteID;
        public short NegativeLength;
        public short Offset;
    }

    public struct TRRoomVertex
    {
        public short X, Y, Z;
        public short Lighting1;
        public ushort Attributes;
        public short Lighting2;
    }

    // Dörtgen Poligon (Quad) - 4 Köşe ve 1 Kaplama (Texture) indexi
    public struct TRFace4
    {
        public ushort V1, V2, V3, V4;
        public ushort Texture;
    }

    // Üçgen Poligon (Triangle) - 3 Köşe ve 1 Kaplama (Texture) indexi
    public struct TRFace3
    {
        public ushort V1, V2, V3;
        public ushort Texture;
    }

    // Sprite (İki boyutlu resimler, alevler vb.)
    public struct TRRoomSprite
    {
        public short Vertex;
        public short Texture;
    }

    public struct TRRoomInfo 
    { 
        public int X, Z, YBottom, YTop;
    }

    public struct TRRoomPortal
    {
        public ushort AdjoiningRoom;
        public TRVertex Normal;
        public TRVertex[] Vertices;
    }

    // Odanın görünmez 2D ızgarasındaki her bir kare (8 byte)
    public struct TRRoomSector
    {
        public ushort FDIndex;   // 2 Byte Zemin verisinin FloorData dizisindeki başlangıç indeksi
        public ushort BoxIndex;  // 2 Byte Yapay zeka (Yol bulma) kutusu
        public byte RoomBelow;   // 1 Byte Eğer çukursa altındaki odanın ID'si (255 ise altı boştur)
        public byte Floor;       // 1 Byte Zeminin yüksekliği
        public byte RoomAbove;   // 1 Byte Eğer tepesi açıksa üstündeki odanın ID'si
        public byte Ceiling;     // 1 Byte Tavanın yüksekliği
    }

    public struct TR2RoomLight
    {
        public int X, Y, Z;
        public ushort Intensity1, Intensity2;
        public uint Fade1, Fade2;
    }

    // Odadaki Statik Obje Yerleşimi (Tam 20 Byte)
    public struct TRRoomStaticMesh
    {
        public int X;             // 4 Byte
        public int Y;             // 4 Byte
        public int Z;             // 4 Byte
        public ushort Rotation;   // 2 Byte (Dönüş açısı)
        public ushort Intensity1; // 2 Byte (1. Işık şiddeti)
        public ushort Intensity2; // 2 Byte (2. Işık şiddeti - TR2'de eklendi)
        public ushort ObjectID;   // 2 Byte (Hangi kalıbı kullanıyor?)
    }

    // Haritaya Yerleştirilmiş Varlıklar (24 byte)
    public struct TR2Entity
    {
        public short TypeID;        // Hangi Model ID'sini kullandığı
        public short Room;          // Hangi odada olduğu
        public int X, Y, Z;         // Haritadaki konumu
        public short Angle;         // Baktığı yön (Döndürme açısı)
        public short Intensity1;    // Işıklandırma
        public short Intensity2;
        public ushort Flags;        // Görünmezlik, tetiklenme gibi ayarlar
    }

    public struct TRMeshTreeNode
    {
        public uint Flags;   // 4 0x01 (Pop): Bir önceki ekleme dön, 0x02 (Push): Mevcut eklemi kaydet
        public int OffsetX;  // 4 Bir önceki parçaya göre X uzaklığı
        public int OffsetY;  // 4 Bir önceki parçaya göre Y uzaklığı
        public int OffsetZ;  // 4 Bir önceki parçaya göre Z uzaklığı
    }

    public struct TRStateChange
    {
        public ushort StateID;
        public ushort NumAnimDispatches;
        public ushort AnimDispatch;
    } // Toplam: 6 Byte

    public struct TRAnimDispatch
    {
        public short Low;
        public short High;
        public short NextAnimation;
        public short NextFrame;
    } // Toplam: 8 Byte

}