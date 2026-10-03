namespace TR2Viewer.Render
{
    // Üstünde durulabilen veya Lara'yı engelleyen bir varlığın (ör. tekne) çarpışma kutusu.
    // Kutu, modelin ilk animasyon karesindeki sınır kutusudur; varlığın yönüyle döndürülür. Ölçüler TR birimi.
    public readonly struct TRSolidBox
    {
        private readonly float _cos, _sin;
        private readonly float _minX, _maxX, _minZ, _maxZ;

        public float X { get; }        // Varlığın kendi konumu ve yönü (kutu buna göre)
        public float Y { get; }
        public float Z { get; }
        public float Angle { get; }
        public bool IsBoat { get; }    // Tekne: üstüne düşen Lara oturur
        public float TopY { get; }     // Kutunun üstü (aşağı pozitif Y)
        public float BottomY { get; }

        public TRSolidBox(float x, float y, float z, float angle, short[] frames, int framePtr, bool isBoat)
        {
            X = x;
            Y = y;
            Z = z;
            Angle = angle;
            IsBoat = isBoat;
            _cos = MathF.Cos(angle);
            _sin = MathF.Sin(angle);
            // Sınır kutusu: minX, maxX, minY, maxY, minZ, maxZ (varlığa göre)
            _minX = frames[framePtr];
            _maxX = frames[framePtr + 1];
            TopY = y + frames[framePtr + 2];
            BottomY = y + frames[framePtr + 3];
            _minZ = frames[framePtr + 4];
            _maxZ = frames[framePtr + 5];
        }

        // (x, z) noktası kutunun içinde mi? Nokta varlığın yerel eksenlerine döndürülerek bakılır.
        public bool Contains(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            float lx = dx * _cos - dz * _sin;
            float lz = dx * _sin + dz * _cos;
            return lx >= _minX && lx <= _maxX && lz >= _minZ && lz <= _maxZ;
        }
    }
}
