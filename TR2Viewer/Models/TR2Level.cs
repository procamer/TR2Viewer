namespace TR2Viewer.Models
{
    public class TR2Level
    {
        public uint Version { get; private set; }
        public TRColor[] Palette { get; private set; }
        public TRColor4[] Palette16 { get; private set; }
        public uint NumTextiles { get; private set; }
        public TRTextile8[] Textiles8 { get; private set; }
        public TRTextile16[] Textiles16 { get; private set; }
        public ushort NumRooms { get; private set; }
        public TR2Room[] Rooms { get; private set; }
        public TRObjectTexture[] ObjectTextures { get; private set; }
        public TRModel[] Models { get; private set; }
        public TR2Entity[] Entities { get; private set; }
        public ushort[] MeshData { get; private set; }
        public uint[] MeshPointers { get; private set; }
        public TRMesh[] Meshes { get; private set; }
        public ushort[] FloorData { get; private set; }
        public TRStaticMeshModel[] StaticMeshModels { get; private set; }
        public TRMeshTreeNode[] MeshTrees { get; private set; }
        public TRAnimation[] Animations { get; private set; }
        public TRStateChange[] StateChanges { get; private set; }        
        public TRAnimDispatch[] AnimDispatches { get; private set; }
        public short[] AnimCommands { get; private set; }
        public short[] Frames { get; private set; }


        public TR2Level(string filePath)
        {
            if (!File.Exists(filePath)) return;

            using BinaryReader reader = new(File.Open(filePath, FileMode.Open));
            // 1. Versiyon
            Version = reader.ReadUInt32();

            // 2. Paletler
            Palette = new TRColor[256];
            for (int i = 0; i < 256; i++) Palette[i] = new TRColor { R = reader.ReadByte(), G = reader.ReadByte(), B = reader.ReadByte() };
            Palette16 = new TRColor4[256];
            for (int i = 0; i < 256; i++) Palette16[i] = new TRColor4 { R = reader.ReadByte(), G = reader.ReadByte(), B = reader.ReadByte(), A = reader.ReadByte() };

            // 3. Tekstürler
            NumTextiles = reader.ReadUInt32();
            Textiles8 = new TRTextile8[NumTextiles];
            for (int i = 0; i < NumTextiles; i++) Textiles8[i].Tile = reader.ReadBytes(256 * 256);
            Textiles16 = new TRTextile16[NumTextiles];
            for (int i = 0; i < NumTextiles; i++)
            {
                Textiles16[i].Tile = new ushort[256 * 256];
                for (int j = 0; j < 256 * 256; j++) Textiles16[i].Tile[j] = reader.ReadUInt16();
            }

            reader.ReadUInt32(); //unused 
            
            // 4. Odaları (Rooms) Okuma
            NumRooms = reader.ReadUInt16();
            Rooms = new TR2Room[NumRooms];
            for (int i = 0; i < NumRooms; i++)
            {
                TR2Room room = new()
                {
                    // a. Oda Bilgisi
                    Info = new TRRoomInfo
                    {
                        X = reader.ReadInt32(),
                        Z = reader.ReadInt32(),
                        YBottom = reader.ReadInt32(),
                        YTop = reader.ReadInt32()
                    },

                    // b. Geometri Verisi (DataWords)
                    NumDataWords = reader.ReadUInt32()
                };
                room.Data = new ushort[room.NumDataWords];
                for (int j = 0; j < room.NumDataWords; j++)
                {
                    room.Data[j] = reader.ReadUInt16();
                }

                // c. Portallar (Bağlantılı diğer odalar)
                room.NumPortals = reader.ReadUInt16();
                room.Portals = new TRRoomPortal[room.NumPortals];
                for (int j = 0; j < room.NumPortals; j++)
                {
                    room.Portals[j] = new TRRoomPortal
                    {
                        AdjoiningRoom = reader.ReadUInt16(),
                        Normal = new TRVertex { X = reader.ReadInt16(), Y = reader.ReadInt16(), Z = reader.ReadInt16() },
                        Vertices = new TRVertex[4]
                    };
                    for (int k = 0; k < 4; k++)
                    {
                        room.Portals[j].Vertices[k] = new TRVertex { X = reader.ReadInt16(), Y = reader.ReadInt16(), Z = reader.ReadInt16() };
                    }
                }

                // d. Sektörler (Floor Data izgarası)
                room.NumZSectors = reader.ReadUInt16();
                room.NumXSectors = reader.ReadUInt16();
                int numSectors = room.NumZSectors * room.NumXSectors;
                room.Sectors = new TRRoomSector[numSectors];
                for (int j = 0; j < numSectors; j++)
                {
                    room.Sectors[j] = new TRRoomSector
                    {
                        FDIndex = reader.ReadUInt16(),
                        BoxIndex = reader.ReadUInt16(),
                        RoomBelow = reader.ReadByte(),
                        Floor = reader.ReadByte(),
                        RoomAbove = reader.ReadByte(),
                        Ceiling = reader.ReadByte()
                    };
                }

                // e. Işıklandırma
                room.AmbientIntensity = reader.ReadInt16();
                room.AmbientIntensity2 = reader.ReadInt16();
                room.LightMode = reader.ReadInt16();

                room.NumLights = reader.ReadUInt16();
                room.Lights = new TR2RoomLight[room.NumLights];
                for (int j = 0; j < room.NumLights; j++)
                {
                    room.Lights[j] = new TR2RoomLight
                    {
                        X = reader.ReadInt32(),
                        Y = reader.ReadInt32(),
                        Z = reader.ReadInt32(),
                        Intensity1 = reader.ReadUInt16(),
                        Intensity2 = reader.ReadUInt16(),
                        Fade1 = reader.ReadUInt32(),
                        Fade2 = reader.ReadUInt32()
                    };
                }

                // f. Statik Objeler (Meshler)
                room.NumStaticMeshes = reader.ReadUInt16();
                room.StaticMeshes = new TRRoomStaticMesh[room.NumStaticMeshes];
                for (int j = 0; j < room.NumStaticMeshes; j++)
                {
                    room.StaticMeshes[j] = new TRRoomStaticMesh
                    {
                        X = reader.ReadInt32(),
                        Y = reader.ReadInt32(),
                        Z = reader.ReadInt32(),
                        Rotation = reader.ReadUInt16(),
                        Intensity1 = reader.ReadUInt16(),
                        Intensity2 = reader.ReadUInt16(),
                        ObjectID = reader.ReadUInt16()
                    };
                }

                // g. Ekstra Oda Bayrakları
                room.AlternateRoom = reader.ReadInt16();
                room.Flags = reader.ReadInt16();

                Rooms[i] = room;
            }

            // Odaların geometrisini (Data dizisini) çözelim
            foreach (var room in Rooms) room.DecodeGeometry();
            
            // 1. Floor Data (Kat Verileri - 2 byte) Zemin Özellikleri ve Eğimler
            uint numFloorData = reader.ReadUInt32();
            FloorData = new ushort[numFloorData];
            for (int i = 0; i < numFloorData; i++) FloorData[i] = reader.ReadUInt16();

            // 2. Mesh Data (Oyun içindeki tüm 3D Modellerin ham poligon verisi - 2 byte ushort)
            uint numMeshData = reader.ReadUInt32();
            MeshData = new ushort[numMeshData];
            for (int i = 0; i < numMeshData; i++) MeshData[i] = reader.ReadUInt16();

            // 3. Mesh Pointers (Hangi modelin MeshData dizisinde nereden başladığını gösteren işaretçiler)
            uint numMeshPointers = reader.ReadUInt32();
            MeshPointers = new uint[numMeshPointers];
            for (int i = 0; i < numMeshPointers; i++) MeshPointers[i] = reader.ReadUInt32();

            // 4. Animations (Animasyon boyutu 32 byte)
            uint numAnimations = reader.ReadUInt32();
            Animations = new TRAnimation[numAnimations];

            for (int i = 0; i < numAnimations; i++)
            {
                Animations[i] = new TRAnimation
                {
                    FrameOffset = reader.ReadUInt32(),
                    FrameRate = reader.ReadByte(),
                    FrameSize = reader.ReadByte(),
                    StateID = reader.ReadUInt16(),
                    Speed = reader.ReadInt32(),
                    Accel = reader.ReadInt32(),
                    FrameStart = reader.ReadUInt16(),
                    FrameEnd = reader.ReadUInt16(),
                    NextAnimation = reader.ReadUInt16(),
                    NextFrame = reader.ReadUInt16(),
                    NumStateChanges = reader.ReadUInt16(),
                    StateChangeOffset = reader.ReadUInt16(),
                    NumAnimCommands = reader.ReadUInt16(),
                    AnimCommandOffset = reader.ReadUInt16()
                };
            }

            // 5. State Changes (Durum Değişiklikleri - 6 byte)
            uint numStateChanges = reader.ReadUInt32();
            StateChanges = new TRStateChange[numStateChanges];
            for (int i = 0; i < numStateChanges; i++)
            {
                StateChanges[i] = new TRStateChange
                {
                    StateID = reader.ReadUInt16(),
                    NumAnimDispatches = reader.ReadUInt16(),
                    AnimDispatch = reader.ReadUInt16()
                };
            }

            // 6. Anim Dispatches (Çöktüğümüz yer - 8 byte)
            uint numAnimDispatches = reader.ReadUInt32();
            AnimDispatches = new TRAnimDispatch[numAnimDispatches];
            for (int i = 0; i < numAnimDispatches; i++)
            {
                AnimDispatches[i] = new TRAnimDispatch
                {
                    Low = reader.ReadInt16(),
                    High = reader.ReadInt16(),
                    NextAnimation = reader.ReadInt16(),
                    NextFrame = reader.ReadInt16()
                };
            }

            // 7. Anim Commands (Animasyon Komutları - 2 byte)
            uint numAnimCommands = reader.ReadUInt32();
            AnimCommands = new short[numAnimCommands];
            for (int i = 0; i < numAnimCommands; i++)
            {
                AnimCommands[i] = reader.ReadInt16();
            }

            // 8. Mesh Trees (Mesh Ağaçları / İskelet Yapısı - 4 byte)
            uint numMeshTrees = reader.ReadUInt32();
            // TR motoru bu değeri "Node (Düğüm)" sayısı olarak değil, 32-bitlik kelime sayısı olarak tutar.
            // Her düğüm 4 kelime (16 byte) olduğu için 4'e bölüyoruz.
            int numNodes = (int)numMeshTrees / 4;

            MeshTrees = new TRMeshTreeNode[numNodes];
            for (int i = 0; i < numNodes; i++)
            {
                MeshTrees[i] = new TRMeshTreeNode
                {
                    Flags = reader.ReadUInt32(),
                    OffsetX = reader.ReadInt32(),
                    OffsetY = reader.ReadInt32(),
                    OffsetZ = reader.ReadInt32()
                };
            }

            // 9. Frames (Animasyon Kareleri ve Obje Yükseklik Kaydırmaları)
            uint numFrames = reader.ReadUInt32();
            Frames = new short[numFrames];
            for (int i = 0; i < numFrames; i++) Frames[i] = reader.ReadInt16();

            // 10. Models (Lara, Düşmanlar, Silahlar vb.)
            uint numModels = reader.ReadUInt32();
            Models = new TRModel[numModels];
            for (int i = 0; i < numModels; i++)
            {
                Models[i] = new TRModel
                {
                    ID = reader.ReadUInt32(),
                    NumMeshes = reader.ReadUInt16(),
                    StartingMesh = reader.ReadUInt16(),
                    MeshTree = reader.ReadUInt32(),
                    FrameOffset = reader.ReadUInt32(),
                    Animation = reader.ReadUInt16()
                };
            }

            // 11. Static Meshes (Heykeller, Meşaleler vb. - 32 byte)
            uint numStaticMeshModels = reader.ReadUInt32();
            StaticMeshModels = new TRStaticMeshModel[numStaticMeshModels];
            for (int i = 0; i < numStaticMeshModels; i++)
            {
                StaticMeshModels[i] = new TRStaticMeshModel
                {
                    ID = reader.ReadUInt32(),
                    Mesh = reader.ReadUInt16(),
                    VisibilityBox = [reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16()],
                    CollisionBox = [reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16()],
                    Flags = reader.ReadUInt16()
                };
            }

            // 12. Object Textures
            uint numObjectTextures = reader.ReadUInt32();
            ObjectTextures = new TRObjectTexture[numObjectTextures];
            for (int i = 0; i < numObjectTextures; i++)
            {
                ObjectTextures[i] = new TRObjectTexture
                {
                    Attribute = reader.ReadUInt16(),
                    TileAndFlag = reader.ReadUInt16(),
                    U = new float[4], V = new float[4]
                };
                for (int j = 0; j < 4; j++)
                {
                    byte xFrac = reader.ReadByte();
                    byte xInt = reader.ReadByte();
                    byte yFrac = reader.ReadByte();
                    byte yInt = reader.ReadByte();
                    // Frac değeri 255 (0xFF) ise, bu kaplamanın sağ veya alt kenarıdır, pikseli 1 birim tamamla
                    ObjectTextures[i].U[j] = (xInt + (xFrac == 255 ? 1.0f : 0.0f)) / 256.0f;
                    ObjectTextures[i].V[j] = (yInt + (yFrac == 255 ? 1.0f : 0.0f)) / 256.0f;
                }
            }

            // 13. Sprite Textures (İki boyutlu resimler - 16 byte)
            uint numSpriteTextures = reader.ReadUInt32();
            reader.BaseStream.Seek(numSpriteTextures * 16, SeekOrigin.Current);

            // 14. Sprite Sequences (8 byte)
            uint numSpriteSequences = reader.ReadUInt32();
            reader.BaseStream.Seek(numSpriteSequences * 8, SeekOrigin.Current);

            // 15. Cameras (Oyun içi kameralar - 16 byte)
            uint numCameras = reader.ReadUInt32();
            reader.BaseStream.Seek(numCameras * 16, SeekOrigin.Current);

            // 16. Sound Sources (Ses kaynakları - 16 byte)
            uint numSoundSources = reader.ReadUInt32();
            reader.BaseStream.Seek(numSoundSources * 16, SeekOrigin.Current);

            // 17. Boxes (Yapay Zeka yol bulma / NavMesh - 8 byte)
            uint numBoxes = reader.ReadUInt32();
            reader.BaseStream.Seek(numBoxes * 8, SeekOrigin.Current);

            // 18. Overlaps (Kutu bağlantıları - 2 byte)
            uint numOverlaps = reader.ReadUInt32();
            reader.BaseStream.Seek(numOverlaps * 2, SeekOrigin.Current);

            // 19. Ground Zones (Bölgeler - TR2'de her kutu için 20 byte)
            reader.BaseStream.Seek(numBoxes * 20, SeekOrigin.Current);

            // 20. Animated Textures (Hareketli Sular/Lavlar vb.)
            uint numAnimatedTextures = reader.ReadUInt32();
            reader.BaseStream.Seek(numAnimatedTextures * 2, SeekOrigin.Current);

            // 21. Entities
            uint numEntities = reader.ReadUInt32();
            Entities = new TR2Entity[numEntities];
            for (int i = 0; i < numEntities; i++)
            {
                Entities[i] = new TR2Entity
                {
                    TypeID = reader.ReadInt16(),
                    Room = reader.ReadInt16(),
                    X = reader.ReadInt32(),
                    Y = reader.ReadInt32(),
                    Z = reader.ReadInt32(),
                    Angle = reader.ReadInt16(),
                    Intensity1 = reader.ReadInt16(),
                    Intensity2 = reader.ReadInt16(),
                    Flags = reader.ReadUInt16()
                };
            }

            DecodeAllMeshes();
        }

        public void DecodeAllMeshes()
        {
            if (MeshPointers == null || MeshData == null) return;

            Meshes = new TRMesh[MeshPointers.Length];
            for (int m = 0; m < MeshPointers.Length; m++)
            {
                try
                {
                    var mesh = new TRMesh();

                    // 1. İşaretçiler (Pointers) 
                    int ptr = (int)(MeshPointers[m] / 2) + 5;

                    // 2. Köşeler (Vertices)
                    short numVertices = (short)MeshData[ptr++];
                    mesh.Vertices = new TRVertex[numVertices];
                    for (int i = 0; i < numVertices; i++)
                    {
                        mesh.Vertices[i] = new TRVertex
                        {
                            X = (short)MeshData[ptr++],
                            Y = (short)MeshData[ptr++],
                            Z = (short)MeshData[ptr++]
                        };
                    }

                    // 3. Normaller veya Işıklandırma Şiddeti
                    short numNormals = (short)MeshData[ptr++];
                    if (numNormals > 0)
                    {
                        ptr += numNormals * 3; // Her normal vektörü 3 short (X,Y,Z) yer kaplar
                    }
                    else
                    {
                        ptr += Math.Abs(numNormals); // Sadece ışık şiddeti (1 short)
                    }

                    // 4. Kaplamalı Dörtgenler (Textured Rectangles)
                    short numTexRects = (short)MeshData[ptr++];
                    mesh.TexturedRectangles = new TRFace4[numTexRects];
                    for (int i = 0; i < numTexRects; i++)
                    {
                        mesh.TexturedRectangles[i] = new TRFace4
                        {
                            V1 = MeshData[ptr++], V2 = MeshData[ptr++], V3 = MeshData[ptr++], V4 = MeshData[ptr++],
                            Texture = MeshData[ptr++]
                        };
                    }

                    // 5. Kaplamalı Üçgenler (Textured Triangles)
                    short numTexTris = (short)MeshData[ptr++];
                    mesh.TexturedTriangles = new TRFace3[numTexTris];
                    for (int i = 0; i < numTexTris; i++)
                    {
                        mesh.TexturedTriangles[i] = new TRFace3
                        {
                            V1 = MeshData[ptr++], V2 = MeshData[ptr++], V3 = MeshData[ptr++],
                            Texture = MeshData[ptr++]
                        };
                    }

                    Meshes[m] = mesh;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HATA] Mesh {m} çözülürken bir sorun oluştu: {ex.Message}");
                }
            }
        }

    }
}