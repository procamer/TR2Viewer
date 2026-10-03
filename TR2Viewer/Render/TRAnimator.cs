using OpenTK.Mathematics;
using TR2Viewer.Models;

namespace TR2Viewer.Render
{
    // Haritadaki bir varlığın (entity) animasyon durumu ve o anki uzuv matrisleri
    public class TRAnimatedEntity
    {
        public TRModel Model;
        public short TypeID;               // Haritadaki varlık tipi (0 = Lara)
        public Matrix4 World;              // Varlığın dünyadaki konumu ve yönü (TR birimi, OpenGL eksenleri)
        public float Light = 1f;
        public int Animation = -1;         // -1 = animasyonsuz (modelin sabit duruşu)
        public float Frame;                // Oyun tiki cinsinden kare numarası
        public Matrix4[] MeshMatrices = []; // Her uzvun yerel koordinattan dünyaya dönüşümü
    }

    // TR animasyon verisini oynatır: anahtar kareler arasında ara değer üretir ve uzuv matrislerini hesaplar.
    public class TRAnimator(TR2Level level)
    {
        private const float TicksPerSecond = 30f; // TR motoru saniyede 30 oyun tiki çalışır

        // Ara değerleme için tekrar kullanılan geçici diziler
        private Quaternion[] _rotA = new Quaternion[64];
        private Quaternion[] _rotB = new Quaternion[64];

        // startAnimation verilmezse modelin varsayılan animasyonu kullanılır
        public void Start(TRAnimatedEntity entity, int? startAnimation = null)
        {
            var model = entity.Model;
            entity.MeshMatrices = new Matrix4[model.NumMeshes];

            int anim = startAnimation ?? model.Animation;
            if (anim >= 0 && anim < level.Animations.Length)
            {
                entity.Animation = anim;
                entity.Frame = level.Animations[anim].FrameStart;
            }

            UpdatePose(entity);
        }

        public void Update(TRAnimatedEntity entity, float deltaSeconds)
        {
            if (entity.Animation < 0) return;

            entity.Frame += deltaSeconds * TicksPerSecond;
            Advance(entity);
            UpdatePose(entity);
        }

        // Animasyon bittiyse NextAnimation/NextFrame zincirini takip et (döngüler de böyle kurulur)
        private void Advance(TRAnimatedEntity entity)
        {
            for (int guard = 0; guard < 16; guard++)
            {
                var anim = level.Animations[entity.Animation];
                if (entity.Frame < anim.FrameEnd + 1) return;

                float overflow = entity.Frame - (anim.FrameEnd + 1);
                if (anim.NextAnimation >= level.Animations.Length)
                {
                    entity.Frame = anim.FrameEnd; // Geçersiz zincir: son karede dur
                    return;
                }

                entity.Animation = anim.NextAnimation;
                entity.Frame = anim.NextFrame + overflow;
            }
        }

        // Varlığın o anki Animation/Frame değerinden uzuv matrislerini hesaplar
        public void UpdatePose(TRAnimatedEntity entity)
        {
            int numMeshes = entity.Model.NumMeshes;
            if (numMeshes == 0) return;
            if (_rotA.Length < numMeshes)
            {
                _rotA = new Quaternion[numMeshes];
                _rotB = new Quaternion[numMeshes];
            }

            Vector3 offset;
            if (entity.Animation < 0)
            {
                // Animasyonsuz model: kendi kare ofsetindeki tek duruş
                if (!ReadKeyframe((int)(entity.Model.FrameOffset / 2), numMeshes, out offset, _rotA)) return;
            }
            else
            {
                var anim = level.Animations[entity.Animation];
                int rate = Math.Max(1, (int)anim.FrameRate); // Kaç tikte bir anahtar kare saklandığı
                float rel = Math.Max(0f, entity.Frame - anim.FrameStart);

                int key = (int)(rel / rate);
                float t = (rel - key * rate) / rate;

                // Son anahtar kare FrameEnd'i kapsayan karedir; ötesine geçme
                int lastKey = (anim.FrameEnd - anim.FrameStart + rate - 1) / rate;
                if (key >= lastKey)
                {
                    key = lastKey;
                    t = 0f;
                }

                int basePtr = (int)(anim.FrameOffset / 2);
                int frameSize = anim.FrameSize; // Bir anahtar karenin short cinsinden boyutu

                if (!ReadKeyframe(basePtr + key * frameSize, numMeshes, out offset, _rotA)) return;

                if (t > 0f && frameSize > 0 && ReadKeyframe(basePtr + (key + 1) * frameSize, numMeshes, out var offsetB, _rotB))
                {
                    offset = Vector3.Lerp(offset, offsetB, t);
                    for (int i = 0; i < numMeshes; i++) _rotA[i] = Quaternion.Slerp(_rotA[i], _rotB[i], t);
                }
            }

            BuildMeshMatrices(entity, offset, _rotA);
        }

        // O anki anahtar karenin sınır kutusundaki en üst nokta (Y, varlığa göre; negatif = yukarı).
        // TR motoru kenara tutunurken ellerin yüksekliğini bununla bulur.
        public bool TryGetTopOfBounds(TRAnimatedEntity entity, out float minY)
        {
            minY = 0f;
            if (entity.Animation < 0) return false;

            var anim = level.Animations[entity.Animation];
            int rate = Math.Max(1, (int)anim.FrameRate);
            int key = (int)(Math.Max(0f, entity.Frame - anim.FrameStart) / rate);
            int ptr = (int)(anim.FrameOffset / 2) + key * anim.FrameSize;

            // Sınır kutusu: minX, maxX, minY, maxY, minZ, maxZ
            if (ptr < 0 || ptr + 6 > level.Frames.Length) return false;
            minY = level.Frames[ptr + 2];
            return true;
        }

        // Bir anahtar kareyi okur: 6 short sınır kutusu, 3 short kök kayması, ardından uzuv başına dönüş
        private bool ReadKeyframe(int ptr, int numMeshes, out Vector3 offset, Quaternion[] rotations)
        {
            offset = Vector3.Zero;
            var frames = level.Frames;

            // Dönüşler 1 veya 2 short olabilir; en kötü durum: 9 short başlık + uzuv başına 2 short
            if (ptr < 0 || ptr + 9 + numMeshes * 2 > frames.Length) return false;

            ptr += 6; // Sınır kutusunu atla
            offset = new Vector3(frames[ptr], frames[ptr + 1], frames[ptr + 2]);
            ptr += 3;

            for (int i = 0; i < numMeshes; i++)
            {
                rotations[i] = GetFrameRotation(frames, ref ptr).ExtractRotation();
            }
            return true;
        }

        // Uzuvları iskelet ağacına (MeshTrees) göre birbirine bağlar
        private void BuildMeshMatrices(TRAnimatedEntity entity, Vector3 rootOffset, Quaternion[] rotations)
        {
            var model = entity.Model;

            // Ana gövdenin (StartingMesh) Matrisi = Kendi Rotasyonu + Kendi Offseti + Dünya Matrisi
            Matrix4 current = Matrix4.CreateFromQuaternion(rotations[0])
                * Matrix4.CreateTranslation(rootOffset.X, -rootOffset.Y, -rootOffset.Z)
                * entity.World;
            entity.MeshMatrices[0] = current;

            Stack<Matrix4> matrixStack = new();
            int treeIndex = (int)(model.MeshTree / 4);

            for (int i = 1; i < model.NumMeshes; i++)
            {
                if (treeIndex >= level.MeshTrees.Length) break;
                var node = level.MeshTrees[treeIndex++];

                // 0x01 (Pop): bir önceki kaydedilen ekleme dön, 0x02 (Push): mevcut eklemi kaydet
                if ((node.Flags & 0x01) != 0 && matrixStack.Count > 0) current = matrixStack.Pop();
                if ((node.Flags & 0x02) != 0) matrixStack.Push(current);

                // Yeni Matris = Animasyon Açısı + Uzvun Eklem Uzaklığı (MeshTree Offset) + Önceki Uzvun Matrisi
                Matrix4 localOffset = Matrix4.CreateTranslation(node.OffsetX, -node.OffsetY, -node.OffsetZ);
                current = Matrix4.CreateFromQuaternion(rotations[i]) * localOffset * current;
                entity.MeshMatrices[i] = current;
            }
        }

        public static Matrix4 GetFrameRotation(short[] frames, ref int offset)
        {
            // TR motoru rotasyonları 16-bit (short) kelimeler halinde okur.
            // İlk iki bit (C000) rotasyonun eksen tipini (X, Y, Z veya Hepsi) belirler.
            ushort w1 = (ushort)frames[offset++];
            int mode = (w1 & 0xC000) >> 14;

            float rotX = 0, rotY = 0, rotZ = 0;
            float rad = (float)Math.PI * 2f / 1024f; // 10-bit değeri (0-1023) radyana çevirme çarpanı

            if (mode == 0) // 3 Eksen birden dönüyorsa (2 kelime okunur)
            {
                ushort w2 = (ushort)frames[offset++];
                int x = (w1 & 0x3FF0) >> 4;
                int y = ((w1 & 0x000F) << 6) | ((w2 & 0xFC00) >> 10);
                int z = (w2 & 0x03FF);

                rotX = x * rad;
                rotY = y * rad;
                rotZ = z * rad;
            }
            else if (mode == 1) rotX = (w1 & 0x03FF) * rad;
            else if (mode == 2) rotY = (w1 & 0x03FF) * rad;
            else if (mode == 3) rotZ = (w1 & 0x03FF) * rad;

            // TR motoru dönüşleri Y-X-Z sırasıyla birleştirir (phd_RotYXZ): köşeye önce Z, sonra X, en son Y uygulanır.
            // OpenTK satır vektörü kullandığı için çarpım sırası Z * X * Y olur.
            return Matrix4.CreateRotationZ(-rotZ) * Matrix4.CreateRotationX(rotX) * Matrix4.CreateRotationY(-rotY);
        }
    }
}
