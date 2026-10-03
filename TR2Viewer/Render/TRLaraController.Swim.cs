using OpenTK.Mathematics;

namespace TR2Viewer.Render
{
    // Lara'nın yüzmesi: su altında 3 boyutlu yüzme (eğilme + yön) ve su yüzünde yüzme.
    // Animasyon geçişlerini yine oyunun durum makinesi verisi yapar; yüzme hızları TR motorunun sabitleridir
    // (bu animasyonların hız alanı 0'dır, hızı kod belirler).
    public partial class TRLaraController
    {
        private enum WaterMode { Above, Surface, Under }

        // Yüzme durumları
        private const int StateTread = 13, StateSwim = 17, StateGlide = 18, StateSurfTread = 33, StateSurfSwim = 34,
                          StateDive = 35, StateSurfBack = 47, StateSurfLeft = 48, StateSurfRight = 49, StateWaterOut = 55;

        // Oyunun kodda sabit kullandığı yüzme animasyonları
        private const int WaterFallAnimation = 112;  // Havadan suya düşüp dalma
        private const int SurfaceAnimation = 114;    // Su altından yüzeye çıkma
        private const int SurfTreadAnimation = 110;  // Su yüzünde bekleme
        private const int SurfDiveAnimation = 119;   // Su yüzünden dalma
        private const int WaterOutAnimation = 111;   // Sudan kenara çıkma
        private const int UnderwaterTreadAnimation = 108; // Su altında bekleme

        private const float SwimMaxSpeed = 200f;     // Su altı en yüksek hız (kulaç başına +8)
        private const float WaterFriction = 6f;      // Süzülürken tik başına yavaşlama
        private const float SurfMaxSpeed = 60f;      // Su yüzünde en yüksek hız
        private const float SwimTurn = 6f * MathF.PI / 180f;  // Su altında tik başına dönüş
        private const float SurfTurn = 4f * MathF.PI / 180f;  // Su yüzünde tik başına dönüş
        private const float PitchStep = 2f * MathF.PI / 180f; // Tik başına eğilme
        private const float MaxPitch = 85f * MathF.PI / 180f;
        private const float WadeDepth = 730f;        // Bundan derin suya yürüyerek girilince yüzmeye geçilir
        private const float ShallowDepth = 700f;     // Su yüzünde bundan sığ yere gelince ayağa kalkılır (WadeDepth ile arada pay)
        private const int DiveTicks = 10;            // Su yüzünde zıplama tuşu bu kadar tik basılı tutulursa dalınır

        private WaterMode _water = WaterMode.Above;
        private float _pitch, _prevPitch;    // Su altında eğilme (pozitif = burun yukarı)
        private float _swimSpeed;
        private float _surfaceY;             // Su yüzeyi (TR Y)
        private int _diveCount;

        private bool IsWaterRoom(int room) => _collision.IsWaterRoom(room);

        // Havadan suya düştü: aşağı doğru eğik dal
        private void EnterWaterFromAir()
        {
            _water = WaterMode.Under;
            _inAir = false;
            _swimSpeed = Math.Clamp(_fallSpeed * 1.5f, 0f, SwimMaxSpeed);
            _fallSpeed = 0f;
            _airSpeed = 0f;
            _pitch = -45f * MathF.PI / 180f;
            Position.Y += 100f;
            SetAnimation(WaterFallAnimation, _level.Animations[WaterFallAnimation].FrameStart);
        }

        // Yürürken derin suya girildi: su yüzünde yüzmeye geç (sığ suda yürümeye devam edilir)
        private void TryEnterWaterFromGround()
        {
            float glX = Position.X / 1024f, glZ = -Position.Z / 1024f;
            if (!_collision.TryGetWaterSurface(Room, glX, glZ, out float surfaceY)) return;
            float floorY = -_collision.GetFloorHeight(Room, glX, glZ) * 1024f;
            if (floorY - surfaceY < WadeDepth) return;

            StartSurface(surfaceY);
            SetAnimation(SurfTreadAnimation, _level.Animations[SurfTreadAnimation].FrameStart);
        }

        private static bool IsWaterState(int state) =>
            state is StateTread or StateSwim or StateGlide or StateSurfTread or StateSurfSwim or StateDive
                or StateSurfBack or StateSurfLeft or StateSurfRight or StateWaterOut;

        private void StartSurface(float surfaceY)
        {
            _water = WaterMode.Surface;
            _surfaceY = surfaceY;
            Position.Y = surfaceY + 1f;
            _pitch = 0f;
            _swimSpeed = 0f;
            _diveCount = 0;
            Room = _collision.ResolveVertical(Room, Position.X / 1024f, -Position.Y / 1024f, -Position.Z / 1024f);
        }

        private void TickWater(LaraInput input)
        {
            // Güvence: suda yüzme dışı bir animasyonda kalınmasın (o animasyonlardan yüzme durumlarına geçiş yoktur)
            if (!IsWaterState(CurrentAnim.StateID))
            {
                int fix = _water == WaterMode.Under ? UnderwaterTreadAnimation : SurfTreadAnimation;
                SetAnimation(fix, _level.Animations[fix].FrameStart);
            }

            var anim = CurrentAnim;
            int state = anim.StateID;
            int goal = _water == WaterMode.Under ? ChooseUnderwaterGoal(state, input) : ChooseSurfaceGoal(state, input);

            // Hedef seçimi animasyonu doğrudan değiştirdiyse (dalma, sudan çıkma) yeni animasyondan devam et
            anim = CurrentAnim;
            state = anim.StateID;
            Animate(anim, state, goal);

            if (_water == WaterMode.Under) MoveUnderwater();
            else if (_water == WaterMode.Surface) MoveSurface();
        }

        // ---------- Su altı ----------

        private int ChooseUnderwaterGoal(int state, LaraInput input)
        {
            // İleri = burun aşağı, geri = burun yukarı (oyundaki gibi), sağ/sol = dön
            if (input.Forward) _pitch -= PitchStep;
            else if (input.Back) _pitch += PitchStep;
            _pitch = Math.Clamp(_pitch, -MaxPitch, MaxPitch);
            if (input.Left) Angle -= SwimTurn;
            else if (input.Right) Angle += SwimTurn;

            switch (state)
            {
                case StateSwim: // Kulaç: hızlan; tuş bırakılınca süzül
                    _swimSpeed = MathF.Min(_swimSpeed + 8f, SwimMaxSpeed);
                    return input.Jump ? StateSwim : StateGlide;
                case StateGlide: // Süzülme: yavaşla; yeterince yavaşlayınca bekle
                    _swimSpeed = MathF.Max(_swimSpeed - WaterFriction, 0f);
                    if (input.Jump) return StateSwim;
                    return _swimSpeed <= SwimMaxSpeed * 2f / 3f ? StateTread : StateGlide;
                case StateTread:
                    _swimSpeed = MathF.Max(_swimSpeed - WaterFriction, 0f);
                    return input.Jump ? StateSwim : StateTread;
                default: // Dalma vb.: animasyon kendi zinciriyle yüzmeye geçer
                    return state;
            }
        }

        private void MoveUnderwater()
        {
            // TR motoru: konum hızın 1/4'ü kadar, eğilme ve yön doğrultusunda ilerler
            float speed = _swimSpeed / 4f;
            float horizontal = MathF.Cos(_pitch) * speed;
            float dx = MathF.Sin(Angle) * horizontal, dz = MathF.Cos(Angle) * horizontal;
            float dy = -MathF.Sin(_pitch) * speed;

            // Yatay: duvar ve yükselen zemin engeller
            if (CanSwimTo(Position.X + dx, Position.Z + dz)) { Position.X += dx; Position.Z += dz; }
            else if (CanSwimTo(Position.X + dx, Position.Z)) Position.X += dx;
            else if (CanSwimTo(Position.X, Position.Z + dz)) Position.Z += dz;
            Room = _collision.ResolvePortals(Room, Position.X / 1024f, -Position.Z / 1024f);

            Position.Y += dy;
            float glX = Position.X / 1024f, glZ = -Position.Z / 1024f;

            // Yüzeye ulaştıysa su yüzüne çık
            if (_collision.TryGetWaterSurface(Room, glX, glZ, out float surfaceY) && Position.Y <= surfaceY + 1f)
            {
                StartSurface(surfaceY);
                SetAnimation(SurfaceAnimation, _level.Animations[SurfaceAnimation].FrameStart);
                return;
            }

            // Zemine ve kapalı tavana girme
            float floorY = -_collision.GetFloorHeight(Room, glX, glZ) * 1024f;
            if (Position.Y > floorY - 100f) Position.Y = floorY - 100f;
            float ceilingY = -_collision.GetCeilingHeight(Room, glX, glZ) * 1024f;
            if (Position.Y < ceilingY + 100f) Position.Y = ceilingY + 100f;

            Room = _collision.ResolveVertical(Room, glX, -Position.Y / 1024f, glZ);

            // Sudan çıkıp havaya geçtiyse (ör. su odasından hava odasına yatay geçiş) düşmeye başla
            if (!IsWaterRoom(Room)) LeaveWater();
        }

        private bool CanSwimTo(float x, float z)
        {
            float glX = x / 1024f, glZ = -z / 1024f;
            int room = _collision.ResolvePortals(Room, glX, glZ);
            if (_collision.IsWall(room, glX, glZ)) return false;
            float floorY = -_collision.GetFloorHeight(room, glX, glZ) * 1024f;
            float ceilingY = -_collision.GetCeilingHeight(room, glX, glZ) * 1024f;
            return floorY > Position.Y && ceilingY < Position.Y; // Zemin veya tavan Lara'nın hizasına geliyorsa duvar gibidir
        }

        private void LeaveWater()
        {
            _water = WaterMode.Above;
            _pitch = 0f;
            _inAir = true;
            _fallSpeed = 0f;
            _airSpeed = 0f;
            SetAnimation(FallAnimation, _level.Animations[FallAnimation].FrameStart);
        }

        // ---------- Su yüzü ----------

        private int ChooseSurfaceGoal(int state, LaraInput input)
        {
            if (state == StateWaterOut) return state;

            // Ctrl: önündeki kenar uygun yükseklikteyse sudan çık
            if (input.Action && TryClimbOutOfWater()) return CurrentAnim.StateID;

            // Zıplama tuşu basılı tutulursa dal
            _diveCount = input.Jump ? _diveCount + 1 : 0;
            if (_diveCount >= DiveTicks)
            {
                Dive();
                return CurrentAnim.StateID;
            }

            bool sidestep = input.Walk && (input.Left || input.Right);
            if (!sidestep)
            {
                if (input.Left) Angle -= SurfTurn;
                else if (input.Right) Angle += SurfTurn;
            }

            switch (state)
            {
                case StateSurfSwim:
                case StateSurfBack:
                case StateSurfLeft:
                case StateSurfRight:
                    _swimSpeed = MathF.Min(_swimSpeed + 8f, SurfMaxSpeed);
                    break;
                default:
                    _swimSpeed = MathF.Max(_swimSpeed - 4f, 0f);
                    break;
            }

            if (input.Forward) return StateSurfSwim;
            if (input.Back) return StateSurfBack;
            if (sidestep) return input.Left ? StateSurfLeft : StateSurfRight;
            return StateSurfTread;
        }

        private static float SurfaceMoveOffset(int state) => state switch
        {
            StateSurfBack => MathF.PI,
            StateSurfLeft => -MathF.PI / 2f,
            StateSurfRight => MathF.PI / 2f,
            _ => 0f
        };

        private void MoveSurface()
        {
            int state = CurrentAnim.StateID;
            if (state == StateWaterOut) return;

            float angle = Angle + SurfaceMoveOffset(state);
            float speed = _swimSpeed / 4f;
            float dx = MathF.Sin(angle) * speed, dz = MathF.Cos(angle) * speed;
            if (dx == 0f && dz == 0f) return;

            // Sığ suya gelindiyse ayağa kalk
            if (TryStandUpInShallowWater(Position.X + dx, Position.Z + dz)) return;

            if (CanSurfaceSwimTo(Position.X + dx, Position.Z + dz, dx, dz)) { Position.X += dx; Position.Z += dz; }
            else if (CanSurfaceSwimTo(Position.X + dx, Position.Z, dx, 0f)) Position.X += dx;
            else if (CanSurfaceSwimTo(Position.X, Position.Z + dz, 0f, dz)) Position.Z += dz;
            else _swimSpeed = 0f;

            Room = _collision.ResolvePortals(Room, Position.X / 1024f, -Position.Z / 1024f);
        }

        // Su yüzünde (x, z) noktasına ve Radius kadar ötesine yüzülebilir mi? Aynı yüzey, yeterli derinlik ve baş üstü boşluk gerekir.
        private bool CanSurfaceSwimTo(float x, float z, float dx, float dz)
        {
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (!IsSurfaceFree(x, z)) return false;
            return len < 0.001f || IsSurfaceFree(x + dx / len * Radius, z + dz / len * Radius);
        }

        private bool IsSurfaceFree(float x, float z)
        {
            float glX = x / 1024f, glZ = -z / 1024f;
            int room = _collision.ResolvePortals(Room, glX, glZ);
            if (_collision.IsWall(room, glX, glZ)) return false;
            if (!_collision.TryGetWaterSurface(room, glX, glZ, out float surfaceY) || MathF.Abs(surfaceY - _surfaceY) > 64f) return false;

            float floorY = -_collision.GetFloorHeight(room, glX, glZ) * 1024f;
            float ceilingY = -_collision.GetCeilingHeight(room, glX, glZ) * 1024f;
            return floorY > _surfaceY + 128f && ceilingY < _surfaceY - 256f;
        }

        // Su yüzeyinin hemen üstündeki (hava) oda. Havuz kenarları çoğu zaman bu odaya aittir;
        // su odasında aynı sektör duvar olarak işaretlidir.
        private int RoomAboveSurface()
        {
            float glX = Position.X / 1024f, glZ = -Position.Z / 1024f;
            return _collision.ResolveVertical(Room, glX, -(_surfaceY - 128f) / 1024f, glZ);
        }

        // Zemin su yüzeyine yeterince yakınsa (sığ su veya kıyı) Lara ayağa kalkar ve yürümeye devam eder.
        // Yüzeyin bir basamaktan fazla üstündeki kenarlar için Ctrl ile çıkılır.
        private bool TryStandUpInShallowWater(float x, float z)
        {
            if (!TryFloorAt(x, z, out float floorY, out int room, RoomAboveSurface())) return false;
            if (floorY - _surfaceY >= ShallowDepth) return false; // Hâlâ derin
            if (floorY < _surfaceY - StepUp) return false;        // Kıyı çok yüksek: Ctrl ile çıkılır
            if (_collision.TryGetFloorSlope(room, x / 1024f, -z / 1024f, out int sz, out int sx) && (Math.Abs(sz) > 2 || Math.Abs(sx) > 2))
                return false;                                      // Dik eğimde ayağa kalkılmaz (geri kayardı)

            Position.X = x;
            Position.Z = z;
            Position.Y = floorY;
            Room = _collision.ResolveVertical(room, x / 1024f, -floorY / 1024f, -z / 1024f);
            _water = WaterMode.Above;
            _pitch = 0f;
            _swimSpeed = 0f;
            SetAnimation(StandAnimation, _level.Animations[StandAnimation].FrameStart);
            return true;
        }

        private void Dive()
        {
            _water = WaterMode.Under;
            _pitch = -45f * MathF.PI / 180f;
            _swimSpeed = 80f;
            _diveCount = 0;
            Position.Y += 100f;
            Room = _collision.ResolveVertical(Room, Position.X / 1024f, -Position.Y / 1024f, -Position.Z / 1024f);
            SetAnimation(SurfDiveAnimation, _level.Animations[SurfDiveAnimation].FrameStart);
        }

        // Ctrl: önündeki kenar suyun en fazla 512 üstünde veya 316 altındaysa sudan çık.
        // Oyundaki gibi Lara doğrudan kenarın üstüne (100 birim içeri) yerleştirilir; animasyon onu görsel olarak sudan çıkarır.
        private bool TryClimbOutOfWater()
        {
            if (!TrySnapAngle(Angle, out float snapped)) return false;

            float fx = Position.X + MathF.Sin(snapped) * (Radius + 64f);
            float fz = Position.Z + MathF.Cos(snapped) * (Radius + 64f);
            // Kenarı yüzeyin üstündeki odadan sorgula (su odasında kenar sektörü duvar görünür)
            if (!TryFloorAt(fx, fz, out float frontY, out int frontRoom, RoomAboveSurface())) return false;

            float hdif = frontY - Position.Y; // Negatif = kenar daha yüksek
            if (hdif <= -512f || hdif > 316f) return false;

            float ceilingY = -_collision.GetCeilingHeight(frontRoom, fx / 1024f, -fz / 1024f) * 1024f;
            if (ceilingY > frontY - Height) return false;

            Angle = snapped;
            int sx = (int)MathF.Round(MathF.Sin(snapped));
            int sz = (int)MathF.Round(MathF.Cos(snapped));
            if (sx != 0)
            {
                float start = MathF.Floor(fx / 1024f) * 1024f;
                Position.X = sx > 0 ? start + Radius : start + 1024f - Radius;
            }
            if (sz != 0)
            {
                float start = MathF.Floor(fz / 1024f) * 1024f;
                Position.Z = sz > 0 ? start + Radius : start + 1024f - Radius;
            }
            Position.Y = frontY;
            Room = _collision.ResolvePortals(Room, Position.X / 1024f, -Position.Z / 1024f);
            Room = _collision.ResolveVertical(Room, Position.X / 1024f, -Position.Y / 1024f, -Position.Z / 1024f);

            _water = WaterMode.Above;
            _pitch = 0f;
            _swimSpeed = 0f;
            SetAnimation(WaterOutAnimation, _level.Animations[WaterOutAnimation].FrameStart);
            return true;
        }
    }
}
