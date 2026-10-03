using OpenTK.Mathematics;
using TR2Viewer.Models;

namespace TR2Viewer.Render
{
    // Oyuncunun bu kare bastığı tuşlar
    public struct LaraInput
    {
        public bool Forward, Back, Left, Right, Walk, Jump, Action;
    }

    // Lara'yı oyundaki gibi yönetir: tuşlar "hedef durum" (goal state) belirler, geçişi oyunun kendi
    // StateChanges/AnimDispatches verisi yapar, hareket hızı ve zıplama hızları animasyon verisinden gelir.
    // Mantık TR motoru gibi saniyede 30 tikte çalışır; çizim için tikler arası ara değer üretilir.
    public partial class TRLaraController
    {
        // TR Lara durum (state) numaraları
        private const int StateWalk = 0, StateRun = 1, StateStop = 2, StateForwardJump = 3, StateFastBack = 5,
                          StateTurnRight = 6, StateTurnLeft = 7, StateFastFall = 9, StateCompress = 15,
                          StateBack = 16, StateBackJump = 25, StateRightJump = 26, StateLeftJump = 27, StateUpJump = 28,
                          StateHang = 10, StateReach = 11, StatePullUp = 19, StateHangLeft = 30, StateHangRight = 31,
                          StateSlide = 24, StateFallBack = 29, StateSlideBack = 32;

        public const int StandAnimation = 11; // Oyun Lara'yı "dur" animasyonuyla başlatır
        private const int FallAnimation = 34; // Kenardan düşerken oyunun (kodda sabit) kullandığı animasyon
        private const int ClimbTwoAnimation = 50;   // Duruştan 2 click (512) yükseğe tırmanma (kodda sabit)
        private const int ClimbThreeAnimation = 42; // Duruştan 3 click (768) yükseğe tırmanma (kodda sabit)
        private const int DropAnimation = 28;       // Kenarı bırakınca düşüş (yukarı zıplama döngüsü)
        private const int SlideAnimation = 70;      // Dik eğimde öne kayma (kodda sabit)
        private const int SlideBackAnimation = 104; // Dik eğimde geriye kayma (kodda sabit)
        private const int FallBackAnimation = 93;   // Geriye kayarken boşluğa düşme (kodda sabit)

        // Animasyon komutları (AnimCommands) ve aldıkları parametre sayısı
        private const int CmdSetPosition = 1, CmdJumpVelocity = 2;
        private static readonly int[] CommandArgCount = [0, 3, 2, 0, 0, 2, 2];

        private const float TickSeconds = 1f / 30f;
        private const float SlowTurn = 4f * MathF.PI / 180f;  // Tik başına dönüş (dururken/yürürken)
        private const float FastTurn = 6f * MathF.PI / 180f;  // Tik başına dönüş (koşarken)
        private const float JumpTurn = 1f * MathF.PI / 180f;  // Havada hafif yön düzeltme
        private const float Radius = 100f;     // Lara'nın duvarlara yaklaşabileceği mesafe (TR birimi)
        private const float Height = 762f;     // Lara'nın boyu (tavan çarpması için)
        private const float StepUp = 256f + 64f; // En fazla 1 click (256) yükseğe adım atabilir
        private const float StepDown = 384f;   // Bundan derin düşüşlerde yere yapışmak yerine düşer
        private const float Gravity = 6f;      // TR motorundaki yerçekimi (birim/tik²)
        private const float FastFallSpeed = 128f;  // Bu hızdan sonra yerçekimi tik başına 1 artırır
        private const float FreefallSpeed = 131f;  // Zıplarken bu hız aşılınca "serbest düşüş" durumuna geçilir

        private readonly TR2Level _level;
        private readonly TRAnimator _animator;
        private readonly TRCollision _collision;

        public TRAnimatedEntity Entity { get; }
        public Vector3 Position;   // TR birimi: X, Y (ayak hizası, aşağı pozitif), Z
        public float Angle;        // TR açısı (radyan): 0 = +Z, pozitif = sağa dönüş
        public int Room;

        private int _frame;
        private float _accumulator;
        private Vector3 _prevPosition;
        private float _prevAngle;

        private bool _inAir;         // Havada mı (zıplama/düşme)
        private float _fallSpeed;    // Dikey hız (negatif = yukarı)
        private float _airSpeed;     // Havadaki yatay hız (zıplama komutundan gelir)
        private float _groundSpeed;  // Son tikteki yerdeki hız (kenardan düşerken korunur)
        private float _airMoveAngle; // Havadaki hareket yönü: kalkış anında sabitlenir (oyundaki move_angle)
        private bool _landed;            // Bu zıplama/düşmeden yere indi, iniş animasyonu bekleniyor
        private int _groundedInAirState; // İndikten sonra iniş geçişi bulunamazsa sayaç

        private bool _hanging;       // Bir kenara asılı (veya yukarı çekiliyor): yerçekimi ve zemin takibi yok
        private float _hangEdgeY;    // Tutunulan kenarın yüksekliği (TR Y)
        private LaraInput _input;    // Bu tikin tuşları
        private float _slideAngle = float.NaN; // Kayılan yokuş aşağı yön (yön değişirse kayma yeniden başlar)

        public TRLaraController(TR2Level level, TRAnimator animator, TRCollision collision, TRAnimatedEntity entity, TR2Entity source)
        {
            _level = level;
            _animator = animator;
            _collision = collision;
            Entity = entity;

            Position = new Vector3(source.X, source.Y, source.Z);
            Angle = source.Angle / 32768f * MathF.PI;
            Room = source.Room;
            _prevPosition = Position;
            _prevAngle = Angle;

            _frame = (int)MathF.Floor(entity.Frame);
            UpdateRender(0f);
        }

        // Çizim için tikler arasındaki ara konum (OpenGL koordinatı, Lara'nın ayak hizası)
        public Vector3 RenderPositionGL { get; private set; }
        public float RenderAngle { get; private set; }

        public void Update(float deltaSeconds, LaraInput input)
        {
            _accumulator += deltaSeconds;
            while (_accumulator >= TickSeconds)
            {
                _prevPosition = Position;
                _prevAngle = Angle;
                _prevPitch = _pitch;
                Tick(input);
                _accumulator -= TickSeconds;
            }
            UpdateRender(_accumulator / TickSeconds);
        }

        private TRAnimation CurrentAnim => _level.Animations[Entity.Animation];

        private static bool IsAirState(int state) =>
            state is StateForwardJump or StateFastFall or StateBackJump or StateRightJump or StateLeftJump or StateUpJump or StateReach or StateFallBack;

        // Duruştan tırmanma animasyonları: Lara duvarın önünde durur, animasyon sonundaki komut onu kenarın üstüne taşır
        private bool IsClimbing => Entity.Animation is ClimbTwoAnimation or ClimbThreeAnimation;

        private void Tick(LaraInput input)
        {
            _input = input;
            if (_water != WaterMode.Above)
            {
                TickWater(input); // Yüzme: TRLaraController.Swim.cs
                return;
            }

            var anim = CurrentAnim;
            int state = anim.StateID;

            // 1. Tuşlara göre hedef durumu ve dönüşü belirle
            int animBefore = Entity.Animation;
            int goal = ChooseGoal(state, input, out float turn);
            Angle += turn;
            if (Entity.Animation != animBefore)
            {
                // Hedef seçimi animasyonu doğrudan değiştirdi (tırmanma, kenarı bırakma)
                anim = CurrentAnim;
                state = anim.StateID;
            }

            // 2. Animasyonu bir tik ilerlet
            Animate(anim, state, goal);

            // İndi ama iniş geçişi bulunamadıysa (veride olmayan bir durum) birkaç tik sonra ayağa kaldır.
            // Sadece gerçek bir inişten sonra sayılır: kalkış animasyonları da "havada" durumundadır ama Lara henüz yerdedir.
            if (_landed && IsAirState(CurrentAnim.StateID))
            {
                if (++_groundedInAirState > 5) SetAnimation(StandAnimation, _level.Animations[StandAnimation].FrameStart);
            }
            else
            {
                _landed = false;
                _groundedInAirState = 0;
            }

            // Asılı kalma; yukarı çekilme bitip ayağa kalkınca sona erer
            if (_hanging && CurrentAnim.StateID is not (StateHang or StatePullUp or StateHangLeft or StateHangRight or StateReach or StateUpJump))
                _hanging = false;

            // 3. Yatay hareket: yerde animasyonun hızı (16.16 sabit noktalı: Speed + Accel * geçen kare), havada zıplama hızı
            anim = CurrentAnim;
            float speed = _inAir ? _airSpeed : (anim.Speed + anim.Accel * (float)(_frame - anim.FrameStart)) / 65536f;
            if (!_inAir) _groundSpeed = speed;

            // Havada yön kalkışta sabittir; sadece ileri zıplama/uzanmada Lara'nın döndüğü yöne gider
            float moveAngle = !_inAir || anim.StateID is StateForwardJump or StateReach
                ? Angle + MoveAngleOffset(anim.StateID)
                : _airMoveAngle;
            if (_hanging)
            {
                // Asılıyken sadece kenar boyunca yana kayılır
                if (anim.StateID is StateHangLeft or StateHangRight) HangMove(MathF.Sin(moveAngle) * speed, MathF.Cos(moveAngle) * speed);
            }
            else if (!Move(MathF.Sin(moveAngle) * speed, MathF.Cos(moveAngle) * speed) && _inAir)
            {
                _airSpeed = 0f; // Havada duvara çarptı
            }

            // 4. Dikey hareket: zemin takibi, zıplama/düşme (asılıyken ve tırmanırken yok)
            if (!_hanging && !IsClimbing)
            {
                UpdateVertical();
                TestSlide();
            }
        }

        // Animasyonu bir tik ilerletir: önce hedef duruma geçiş, yoksa animasyon sonunda komutlar ve zincir
        private void Animate(TRAnimation anim, int state, int goal)
        {
            _frame++;
            if (goal != state && TryGetDispatch(anim, goal, _frame, out int nextAnim, out int nextFrame))
            {
                SetAnimation(nextAnim, nextFrame);
            }
            else if (_frame > anim.FrameEnd)
            {
                RunEndCommands(anim);
                if (anim.NextAnimation < _level.Animations.Length) SetAnimation(anim.NextAnimation, anim.NextFrame);
            }
        }

        // Asılıyken hedef durum: Ctrl bırakılırsa düş, ileri = yukarı çekil, sağ/sol = kenar boyunca kay
        private int ChooseHangGoal(int state, LaraInput input)
        {
            switch (state)
            {
                case StateReach:
                case StateUpJump:
                    return StateHang; // Kenar yakalandı: asılı animasyonuna geç
                case StateHang:
                case StateHangLeft:
                case StateHangRight:
                    if (!input.Action)
                    {
                        // Kenarı bırak: oyundaki gibi yukarı zıplama düşüşüyle aşağı in
                        _hanging = false;
                        _inAir = true;
                        _fallSpeed = 0f;
                        _airSpeed = 0f;
                        SetAnimation(DropAnimation, _level.Animations[DropAnimation].FrameStart);
                        return StateUpJump;
                    }
                    if (state == StateHang && input.Forward && CanPullUp()) return StatePullUp;
                    if (input.Left) return StateHangLeft;
                    if (input.Right) return StateHangRight;
                    return StateHang;
                default:
                    return state; // Yukarı çekilme animasyonları kendi zinciriyle ilerler
            }
        }

        private int ChooseGoal(int state, LaraInput input, out float turn)
        {
            float turnDir = (input.Right ? 1f : 0f) - (input.Left ? 1f : 0f);
            turn = 0f;

            if (_hanging) return ChooseHangGoal(state, input);

            if (_inAir)
            {
                // Havada: durum korunur; çok hızlı düşülüyorsa serbest düşüşe geç; Ctrl ile kenara uzan
                turn = state == StateForwardJump ? turnDir * JumpTurn : 0f;
                if (state != StateFastFall && _fallSpeed > FreefallSpeed) return StateFastFall;
                if (state == StateForwardJump && input.Action) return StateReach;
                return state;
            }

            switch (state)
            {
                case StateStop:
                case StateTurnLeft:
                case StateTurnRight:
                    if (state != StateStop) turn = turnDir * SlowTurn;
                    if (input.Action && TryClimb()) return CurrentAnim.StateID;
                    if (input.Jump) return StateCompress;
                    if (input.Forward && !IsBlockedAhead()) return input.Walk ? StateWalk : StateRun;
                    if (input.Back) return input.Walk ? StateBack : StateFastBack;
                    if (turnDir < 0) return StateTurnLeft;
                    if (turnDir > 0) return StateTurnRight;
                    return StateStop;

                case StateWalk:
                    turn = turnDir * SlowTurn;
                    return input.Forward ? (input.Walk ? StateWalk : StateRun) : StateStop;

                case StateRun:
                    turn = turnDir * FastTurn;
                    if (input.Jump) return StateForwardJump;
                    return input.Forward ? (input.Walk ? StateWalk : StateRun) : StateStop;

                case StateBack:
                    turn = turnDir * SlowTurn;
                    return input.Back && input.Walk ? StateBack : StateStop;

                case StateFastBack:
                    turn = turnDir * SlowTurn;
                    return StateStop;

                case StateSlide:
                    // Kayarken zıplanabilir; eğim bitince durulur
                    if (input.Jump) return StateForwardJump;
                    return TryGetSteepSlope(out _) ? StateSlide : StateStop;

                case StateSlideBack:
                    if (input.Jump) return StateBackJump;
                    return TryGetSteepSlope(out _) ? StateSlideBack : StateStop;

                case StateCompress:
                    // Zıplama yönü: tuşa göre; tuş yoksa hazırlık animasyonu yukarı zıplamaya zincirlenir
                    if (input.Forward) return StateForwardJump;
                    if (input.Back) return StateBackJump;
                    if (input.Left) return StateLeftJump;
                    if (input.Right) return StateRightJump;
                    return StateCompress;

                default:
                    // Havadaki bir durumdayken yere inildi: iniş animasyonuna geç (ileri basılıysa koşarak)
                    if (IsAirState(state)) return input.Forward && !input.Walk ? StateRun : StateStop;
                    return StateStop;
            }
        }

        // Hareket yönü Lara'nın baktığı yöne göre: geri ve yan zıplamalar farklı yöne gider
        private static float MoveAngleOffset(int state) => state switch
        {
            StateFastBack or StateBack or StateBackJump or StateSlideBack or StateFallBack => MathF.PI,
            StateRightJump or StateHangRight => MathF.PI / 2f,
            StateLeftJump or StateHangLeft => -MathF.PI / 2f,
            _ => 0f
        };

        private void SetAnimation(int animation, int frame)
        {
            Entity.Animation = animation;
            _frame = frame;
        }

        // Animasyonun durum değişikliklerinde hedef durum için şu anki kareyi kapsayan geçişi ara
        private bool TryGetDispatch(TRAnimation anim, int goal, int frame, out int nextAnim, out int nextFrame)
        {
            nextAnim = nextFrame = 0;
            for (int i = 0; i < anim.NumStateChanges; i++)
            {
                int scIndex = anim.StateChangeOffset + i;
                if (scIndex >= _level.StateChanges.Length) break;
                var sc = _level.StateChanges[scIndex];
                if (sc.StateID != goal) continue;

                for (int j = 0; j < sc.NumAnimDispatches; j++)
                {
                    int dIndex = sc.AnimDispatch + j;
                    if (dIndex >= _level.AnimDispatches.Length) break;
                    var d = _level.AnimDispatches[dIndex];
                    if (frame >= d.Low && frame <= d.High && d.NextAnimation >= 0 && d.NextAnimation < _level.Animations.Length)
                    {
                        nextAnim = d.NextAnimation;
                        nextFrame = d.NextFrame;
                        return true;
                    }
                }
            }
            return false;
        }

        // Animasyon bittiğinde çalışan komutlar: konum kaydırma ve zıplama hızı. (Ses/efekt komutları atlanır.)
        private void RunEndCommands(TRAnimation anim)
        {
            var cmds = _level.AnimCommands;
            int p = anim.AnimCommandOffset;
            for (int c = 0; c < anim.NumAnimCommands && p < cmds.Length; c++)
            {
                int type = cmds[p++];
                if (type < 1 || type >= CommandArgCount.Length || p + CommandArgCount[type] > cmds.Length) return;

                if (type == CmdSetPosition)
                {
                    // Lara'nın baktığı yöne göre yerel kaydırma (x = sağ, z = ileri)
                    float x = cmds[p], y = cmds[p + 1], z = cmds[p + 2];
                    float s = MathF.Sin(Angle), co = MathF.Cos(Angle);
                    Position += new Vector3(co * x + s * z, y, -s * x + co * z);
                }
                else if (type == CmdJumpVelocity)
                {
                    _fallSpeed = cmds[p];     // Negatif = yukarı
                    _airSpeed = cmds[p + 1];  // Yatay hız
                    _airMoveAngle = Angle + MoveAngleOffset(anim.StateID); // Kalkış animasyonunun yönü (geri/yan zıplama)
                    _inAir = true;
                }
                p += CommandArgCount[type];
            }
        }

        // Hareketi uygular; tamamen engellendiyse false döner
        private bool Move(float dx, float dz)
        {
            if (dx == 0f && dz == 0f) return true;

            // Önce tam hareketi dene; duvara çarparsa duvar boyunca kaymak için eksenleri ayrı dene
            bool moved = true;
            if (CanStand(Position.X + dx, Position.Z + dz, dx, dz)) { Position.X += dx; Position.Z += dz; }
            else if (CanStand(Position.X + dx, Position.Z, dx, 0f)) Position.X += dx;
            else if (CanStand(Position.X, Position.Z + dz, 0f, dz)) Position.Z += dz;
            else moved = false;

            Room = _collision.ResolvePortals(Room, Position.X / 1024f, -Position.Z / 1024f);
            return moved;
        }

        // Lara (x, z) noktasında durabilir mi? Hareket yönünde Radius kadar ötesini de kontrol eder.
        private bool CanStand(float x, float z, float dx, float dz)
        {
            if (!IsFree(x, z)) return false;

            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 0.001f) return true;
            return IsFree(x + dx / len * Radius, z + dz / len * Radius);
        }

        private bool IsFree(float x, float z)
        {
            float glX = x / 1024f, glZ = -z / 1024f;
            int room = _collision.ResolvePortals(Room, glX, glZ);
            if (_collision.IsWall(room, glX, glZ)) return false;

            float floorGL = _collision.GetFloorHeight(room, glX, glZ);
            if (floorGL == -9999f) return false;

            // Yerde 1 click'lik basamak çıkılabilir; havada ayakların üstündeki zemin duvar sayılır
            float floorY = -floorGL * 1024f;              // TR Y'si (aşağı pozitif)
            return floorY >= Position.Y - (_inAir ? 0f : StepUp);
        }

        // (x, z) noktasındaki zemin yüksekliği (TR Y); duvar veya oda dışıysa false
        private bool TryFloorAt(float x, float z, out float floorY, out int room)
        {
            floorY = 0f;
            float glX = x / 1024f, glZ = -z / 1024f;
            room = _collision.ResolvePortals(Room, glX, glZ);
            if (_collision.IsWall(room, glX, glZ)) return false;

            float floorGL = _collision.GetFloorHeight(room, glX, glZ);
            if (floorGL == -9999f) return false;
            floorY = -floorGL * 1024f;
            return true;
        }

        // Tutunma ve tırmanma sadece duvara yaklaşık dik bakarken olur (oyunda ±35°): açıyı en yakın eksene oturt
        private static bool TrySnapAngle(float angle, out float snapped)
        {
            const float quarter = MathF.PI / 2f;
            snapped = MathF.Round(angle / quarter) * quarter;
            return MathF.Abs(angle - snapped) <= 35f * MathF.PI / 180f;
        }

        // Lara'yı önündeki yüksek sektörün kenarına, Radius kadar mesafeye yerleştir
        private void SnapToWall(float snappedAngle, float frontX, float frontZ)
        {
            int sx = (int)MathF.Round(MathF.Sin(snappedAngle));
            int sz = (int)MathF.Round(MathF.Cos(snappedAngle));
            if (sx != 0)
            {
                float start = MathF.Floor(frontX / 1024f) * 1024f;
                Position.X = sx > 0 ? start - Radius : start + 1024f + Radius;
            }
            if (sz != 0)
            {
                float start = MathF.Floor(frontZ / 1024f) * 1024f;
                Position.Z = sz > 0 ? start - Radius : start + 1024f + Radius;
            }
            Room = _collision.ResolvePortals(Room, Position.X / 1024f, -Position.Z / 1024f);
        }

        // Havada (uzanırken veya yukarı zıplarken) eller bu tik önündeki kenarın hizasından geçtiyse tutun
        private bool TryCatchEdge(float prevY)
        {
            if (!TrySnapAngle(Angle, out float snapped)) return false;

            float fx = Position.X + MathF.Sin(snapped) * (Radius + 64f);
            float fz = Position.Z + MathF.Cos(snapped) * (Radius + 64f);
            if (!TryFloorAt(fx, fz, out float edgeY, out _)) return false;

            // Ellerin yüksekliği: animasyon karesinin sınır kutusunun tepesi
            if (!_animator.TryGetTopOfBounds(Entity, out float top)) return false;
            float handsPrev = prevY + top, handsNow = Position.Y + top;
            if (handsPrev > edgeY || handsNow < edgeY) return false; // Eller kenarı aşağı doğru geçmedi

            Angle = snapped;
            SnapToWall(snapped, fx, fz);
            Position.Y = edgeY - top; // Eller tam kenarda

            _inAir = false;
            _fallSpeed = 0f;
            _airSpeed = 0f;
            _hanging = true;
            _hangEdgeY = edgeY;
            return true;
        }

        // Kenarın üstünde ayakta duracak kadar yer varsa yukarı çekilebilir
        private bool CanPullUp()
        {
            float fx = Position.X + MathF.Sin(Angle) * (Radius + 64f);
            float fz = Position.Z + MathF.Cos(Angle) * (Radius + 64f);
            if (!TryFloorAt(fx, fz, out _, out int room)) return false;
            float ceilingY = -_collision.GetCeilingHeight(room, fx / 1024f, -fz / 1024f) * 1024f;
            return ceilingY <= _hangEdgeY - Height;
        }

        // Asılıyken kenar boyunca kay: yeni konumda da aynı yükseklikte bir kenar olmalı
        private void HangMove(float dx, float dz)
        {
            if (dx == 0f && dz == 0f) return;
            float nx = Position.X + dx, nz = Position.Z + dz;

            // Kenarın bittiği yere Radius kadar kala dur
            float len = MathF.Sqrt(dx * dx + dz * dz);
            float px = nx + dx / len * Radius, pz = nz + dz / len * Radius;
            float fx = MathF.Sin(Angle) * (Radius + 64f), fz = MathF.Cos(Angle) * (Radius + 64f);

            if (!TryFloorAt(px + fx, pz + fz, out float edgeY, out _) || MathF.Abs(edgeY - _hangEdgeY) > 64f) return;
            if (_collision.IsWall(_collision.ResolvePortals(Room, px / 1024f, -pz / 1024f), px / 1024f, -pz / 1024f)) return;

            Position.X = nx;
            Position.Z = nz;
            Room = _collision.ResolvePortals(Room, Position.X / 1024f, -Position.Z / 1024f);
        }

        // Duruştan Ctrl: önündeki duvar 2 veya 3 click yüksekse üstüne tırman (oyunun kodda sabit tırmanma animasyonları)
        private bool TryClimb()
        {
            if (!TrySnapAngle(Angle, out float snapped)) return false;

            float fx = Position.X + MathF.Sin(snapped) * (Radius + 64f);
            float fz = Position.Z + MathF.Cos(snapped) * (Radius + 64f);
            if (!TryFloorAt(fx, fz, out float frontY, out int frontRoom)) return false;

            float hdif = frontY - Position.Y; // Negatif = önü daha yüksek
            int climbAnim;
            float climb;
            if (hdif <= -384f && hdif > -640f) { climbAnim = ClimbTwoAnimation; climb = 512f; }
            else if (hdif <= -640f && hdif > -896f) { climbAnim = ClimbThreeAnimation; climb = 768f; }
            else return false;

            // Kenarın üstünde ayakta duracak yer olmalı
            float ceilingY = -_collision.GetCeilingHeight(frontRoom, fx / 1024f, -fz / 1024f) * 1024f;
            if (ceilingY > frontY - Height) return false;

            Angle = snapped;
            SnapToWall(snapped, fx, fz);
            Position.Y = frontY + climb; // Animasyon sonundaki komut tam "climb" kadar yukarı taşır
            SetAnimation(climbAnim, _level.Animations[climbAnim].FrameStart);
            return true;
        }

        // Lara dik bir eğimde mi (sektör boyunca 2 click'ten fazla)? Öyleyse yokuş aşağı yönü döndürür.
        // Yön TR motoru gibi eksenlere oturtulur: eğimin baskın olduğu eksen seçilir.
        private bool TryGetSteepSlope(out float downhill)
        {
            downhill = 0f;
            float glX = Position.X / 1024f, glZ = -Position.Z / 1024f;
            if (!_collision.TryGetFloorSlope(Room, glX, glZ, out int slopeZ, out int slopeX)) return false;
            if (Math.Abs(slopeZ) <= 2 && Math.Abs(slopeX) <= 2) return false;

            // Zemin yüksekliği (aşağı pozitif) X'te -slopeX/4, Z'de -slopeZ/4 oranında artar: yokuş aşağı bu yöndür
            float gx = -slopeX, gz = -slopeZ;
            if (MathF.Abs(gz) > MathF.Abs(gx)) downhill = gz > 0 ? 0f : MathF.PI;
            else downhill = gx > 0 ? MathF.PI / 2f : -MathF.PI / 2f;
            return true;
        }

        // Dik eğime basıldıysa kaymaya başla: yokuş aşağı bakıyorsa öne, yukarı bakıyorsa geriye kayar
        private void TestSlide()
        {
            int state = CurrentAnim.StateID;
            if (_inAir || IsAirState(state) || state == StateCompress) return;
            if (!TryGetSteepSlope(out float downhill)) return;

            float diff = WrapAngle(downhill - Angle);
            if (MathF.Abs(diff) <= MathF.PI / 2f)
            {
                if (state == StateSlide && _slideAngle == downhill) return;
                Angle += diff; // Yokuş aşağı dön (en kısa yoldan)
                SetAnimation(SlideAnimation, _level.Animations[SlideAnimation].FrameStart);
            }
            else
            {
                if (state == StateSlideBack && _slideAngle == downhill) return;
                Angle += WrapAngle(downhill + MathF.PI - Angle); // Yokuş yukarı bak
                SetAnimation(SlideBackAnimation, _level.Animations[SlideBackAnimation].FrameStart);
            }
            _slideAngle = downhill;
        }

        private static float WrapAngle(float a)
        {
            a %= MathF.Tau;
            if (a > MathF.PI) a -= MathF.Tau;
            if (a < -MathF.PI) a += MathF.Tau;
            return a;
        }

        private bool IsBlockedAhead()
        {
            float x = Position.X + MathF.Sin(Angle) * (Radius + 64f);
            float z = Position.Z + MathF.Cos(Angle) * (Radius + 64f);
            return !IsFree(x, z);
        }

        private void UpdateVertical()
        {
            float glX = Position.X / 1024f, glZ = -Position.Z / 1024f;
            float floorGL = _collision.GetFloorHeight(Room, glX, glZ);
            if (floorGL == -9999f) return;
            float floorY = -floorGL * 1024f;

            if (!_inAir && floorY > Position.Y + StepDown)
            {
                // Kenardan boşluğa adım atıldı: hızını koruyarak düşmeye başla
                _inAir = true;
                _fallSpeed = 0f;
                _airSpeed = _groundSpeed;
                _airMoveAngle = Angle + MoveAngleOffset(CurrentAnim.StateID); // Düşmeden önceki hareket yönü
                int fall = CurrentAnim.StateID == StateSlideBack ? FallBackAnimation : FallAnimation;
                if (fall < _level.Animations.Length) SetAnimation(fall, _level.Animations[fall].FrameStart);
            }

            if (_inAir)
            {
                float prevY = Position.Y;
                _fallSpeed += _fallSpeed < FastFallSpeed ? Gravity : 1f;
                Position.Y += _fallSpeed;

                // Ctrl basılıyken uzanma veya yukarı zıplama sırasında aşağı inerken kenara tutun
                if (_input.Action && _fallSpeed > 0f && CurrentAnim.StateID is (StateReach or StateUpJump) && TryCatchEdge(prevY)) return;

                // Suya düştüyse dal (ayaklar su yüzeyinin altına indi)
                Room = _collision.ResolveVertical(Room, glX, -Position.Y / 1024f, glZ);
                if (IsWaterRoom(Room))
                {
                    EnterWaterFromAir();
                    return;
                }

                // Tavana çarptıysa geri sek
                float ceilingY = -_collision.GetCeilingHeight(Room, glX, glZ) * 1024f;
                if (_fallSpeed < 0f && Position.Y - Height < ceilingY)
                {
                    Position.Y = ceilingY + Height;
                    _fallSpeed = 1f;
                }

                // Zemine indi
                if (_fallSpeed > 0f && Position.Y >= floorY)
                {
                    Position.Y = floorY;
                    _inAir = false;
                    _landed = true;
                    _fallSpeed = 0f;
                    _airSpeed = 0f;
                }
            }
            else
            {
                Position.Y = floorY; // Zemine yapış (basamak ve eğimlerde)
            }

            Room = _collision.ResolveVertical(Room, glX, -Position.Y / 1024f, glZ);

            // Yürürken derin suya girildiyse su yüzünde yüzmeye geç
            if (!_inAir && IsWaterRoom(Room)) TryEnterWaterFromGround();
        }

        private void UpdateRender(float t)
        {
            Vector3 pos = Vector3.Lerp(_prevPosition, Position, t);
            float angle = _prevAngle + (Angle - _prevAngle) * t;
            float pitch = _prevPitch + (_pitch - _prevPitch) * t;

            RenderPositionGL = new Vector3(pos.X / 1024f, -pos.Y / 1024f, -pos.Z / 1024f);
            RenderAngle = angle;

            // TR sırası: önce eğilme (X), sonra yön (Y); OpenGL'de Y ve Z ters olduğu için yön açısı eksi
            Entity.World = Matrix4.CreateRotationX(pitch) * Matrix4.CreateRotationY(-angle) * Matrix4.CreateTranslation(pos.X, -pos.Y, -pos.Z);
            Entity.Frame = _frame + t; // Kareler arası ara değer (animatör anahtar kareler arasında yumuşatır)
            _animator.UpdatePose(Entity);
        }
    }
}
