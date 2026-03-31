namespace TR2Viewer.Models
{
    public class TR2Room
    {
        public TRRoomInfo Info;
        public uint NumDataWords;
        public ushort[] Data;

        //public TRRoomData RoomData; // Geometri verileri (köşeler, yüzler, spriteler)
        public TRRoomVertex[] Vertices;
        public TRFace4[] Rectangles;
        public TRFace3[] Triangles;
        public TRRoomSprite[] Sprites;

        public ushort NumPortals;
        public TRRoomPortal[] Portals;

        public ushort NumZSectors, NumXSectors;
        public TRRoomSector[] Sectors;

        public short AmbientIntensity, AmbientIntensity2, LightMode;

        public ushort NumLights;
        public TR2RoomLight[] Lights;

        public ushort NumStaticMeshes;
        public TRRoomStaticMesh[] StaticMeshes;

        public short AlternateRoom, Flags;

        public void DecodeGeometry()
        {
            if (Data == null || Data.Length == 0) return;

            int ptr = 0;
            // 1. Köşeler (Vertices)
            short numVertices = (short)Data[ptr++];
            Vertices = new TRRoomVertex[numVertices];
            for (int i = 0; i < numVertices; i++)
            {
                Vertices[i] = new TRRoomVertex
                {
                    X = (short)Data[ptr++],
                    Y = (short)Data[ptr++],
                    Z = (short)Data[ptr++],
                    Lighting1 = (short)Data[ptr++],
                    Attributes = Data[ptr++],
                    Lighting2 = (short)Data[ptr++]
                };
            }

            // 2. Dörtgenler (Rectangles / Quads)
            short numRectangles = (short)Data[ptr++];
            Rectangles = new TRFace4[numRectangles];
            for (int i = 0; i < numRectangles; i++)
            {
                Rectangles[i] = new TRFace4
                {
                    V1 = Data[ptr++],
                    V2 = Data[ptr++],
                    V3 = Data[ptr++],
                    V4 = Data[ptr++],
                    Texture = Data[ptr++]
                };
            }

            // 3. Üçgenler (Triangles)
            short numTriangles = (short)Data[ptr++];
            Triangles = new TRFace3[numTriangles];
            for (int i = 0; i < numTriangles; i++)
            {
                Triangles[i] = new TRFace3
                {
                    V1 = Data[ptr++],
                    V2 = Data[ptr++],
                    V3 = Data[ptr++],
                    Texture = Data[ptr++]
                };
            }

            // 4. Spriteler (Sprites)
            short numSprites = (short)Data[ptr++];
            Sprites = new TRRoomSprite[numSprites];
            for (int i = 0; i < numSprites; i++)
            {
                Sprites[i] = new TRRoomSprite
                {
                    Vertex = (short)Data[ptr++],
                    Texture = (short)Data[ptr++]
                };
            }
        }

    }
}
