namespace TR2Viewer.Render
{
    // Tırmanılabilir duvarlar (el merdivenleri). Duvara Ctrl ile tutunulur; yukarı/aşağı tırmanma animasyonları
    // her turun sonunda konum komutuyla Lara'yı 512 (durunca 256) birim taşır, yana tırmanmada hız animasyondan gelir.
    public partial class TRLaraController
    {
        private const int StateClimbStance = 56, StateClimbing = 57, StateClimbLeft = 58, StateClimbRight = 60, StateClimbDown = 61;
        private const int ClimbStanceAnimation = 164; // Duvarda tutunarak bekleme
        private const int UpJumpStartAnimation = 26;  // Duruştan yukarı zıplama (sonunda zıplama komutu var)
        private const float ClimbStep = 512f;         // Bir tırmanma turunun taşıdığı yükseklik

        private bool _climbing;

        // Lara'nın (x, z) noktasındaki sektörde, verilen yöndeki duvar tırmanılabilir mi?
        private bool IsClimbableFacing(float x, float z, float angle)
        {
            int dir = ((int)MathF.Round(angle / (MathF.PI / 2f)) % 4 + 4) % 4; // 0 = +Z, 1 = +X, 2 = -Z, 3 = -X
            int room = _collision.ResolvePortals(Room, x / 1024f, -z / 1024f);
            return (_collision.GetClimbFlags(room, x / 1024f, -z / 1024f) & (1 << dir)) != 0;
        }

        // Önünde (Radius + 64 ileride) tırmanılabilir bir duvar var mı? Duvarın üstü (kenar) yüksekliği de döner.
        private bool TryGetClimbWall(out float snapped, out float wallTopY)
        {
            wallTopY = float.MinValue;
            if (!TrySnapAngle(Angle, out snapped) || !IsClimbableFacing(Position.X, Position.Z, snapped)) return false;

            float fx = Position.X + MathF.Sin(snapped) * (Radius + 64f);
            float fz = Position.Z + MathF.Cos(snapped) * (Radius + 64f);
            if (TryFloorAt(fx, fz, out float frontY, out _)) wallTopY = frontY; // Duvar değil de yüksek bir zeminse üstü odur
            return wallTopY < Position.Y - 128f; // Önü ayaklardan yüksek olmalı (tepeye yaklaşınca kenar 256 yukarıda kalır)
        }

        // Havada (yukarı zıplarken/uzanırken) Ctrl basılıyken önündeki tırmanılabilir duvara tutun
        private bool TryCatchClimbWall()
        {
            if (!TryGetClimbWall(out float snapped, out _)) return false;

            float fx = Position.X + MathF.Sin(snapped) * (Radius + 64f);
            float fz = Position.Z + MathF.Cos(snapped) * (Radius + 64f);
            Angle = snapped;
            SnapToWall(snapped, fx, fz);
            Position.Y = MathF.Round(Position.Y / 256f) * 256f; // Tırmanma 256'lık adımlarla ilerler; kenarlarla hizalı kalsın

            _inAir = false;
            _fallSpeed = 0f;
            _airSpeed = 0f;
            StartClimbing();
            SetAnimation(ClimbStanceAnimation, _level.Animations[ClimbStanceAnimation].FrameStart);
            return true;
        }

        private void StartClimbing()
        {
            _climbing = true;
            _hanging = false;
        }

        private void TickClimb(LaraInput input)
        {
            var anim = CurrentAnim;
            int state = anim.StateID;

            // Ctrl bırakılırsa düş (oyundaki gibi yukarı zıplama düşüşüyle)
            if (!input.Action && state is StateClimbStance or StateClimbing or StateClimbDown or StateClimbLeft or StateClimbRight)
            {
                DropFromWall();
                return;
            }

            int goal = state;
            switch (state)
            {
                case StateClimbStance:
                    if (input.Jump) goal = StateBackJump; // Sırtüstü atlayarak in
                    else if (input.Forward)
                    {
                        if (CanClimbOver(out float topY))
                        {
                            // Tırmanma 256'lık adımlarla durur; kenarın üstüne çıkmadan önce kenara hizala (oyundaki gibi küçük düzeltme)
                            Position.Y = topY + ClimbStep;
                            goal = StatePullUp;
                        }
                        else if (CanClimbUp()) goal = StateClimbing;
                    }
                    else if (input.Back)
                    {
                        if (CanClimbDown()) goal = StateClimbDown;
                        else
                        {
                            DropFromWall(); // Zemine yakın: bırak, ayakların üstüne düş
                            return;
                        }
                    }
                    else if (input.Left && CanClimbSide(-1f)) goal = StateClimbLeft;
                    else if (input.Right && CanClimbSide(1f)) goal = StateClimbRight;
                    break;
                case StateClimbing:
                    goal = input.Forward && CanClimbUp() ? StateClimbing : StateClimbStance;
                    break;
                case StateClimbDown:
                    goal = input.Back && CanClimbDown() ? StateClimbDown : StateClimbStance;
                    break;
                case StateClimbLeft:
                    goal = input.Left && CanClimbSide(-1f) ? StateClimbLeft : StateClimbStance;
                    break;
                case StateClimbRight:
                    goal = input.Right && CanClimbSide(1f) ? StateClimbRight : StateClimbStance;
                    break;
            }

            Animate(anim, state, goal);

            // Yana tırmanma: animasyonun hızıyla duvar boyunca kay
            anim = CurrentAnim;
            if (anim.StateID is StateClimbLeft or StateClimbRight)
            {
                float side = anim.StateID == StateClimbLeft ? -1f : 1f;
                float speed = (anim.Speed + anim.Accel * (float)(_frame - anim.FrameStart)) / 65536f;
                if (CanClimbSide(side))
                {
                    Position.X += MathF.Sin(Angle + side * MathF.PI / 2f) * speed;
                    Position.Z += MathF.Cos(Angle + side * MathF.PI / 2f) * speed;
                }
            }

            // Gövde ortasına göre oda (yukarı/aşağı tırmanırken oda değişebilir)
            Room = _collision.ResolvePortals(Room, Position.X / 1024f, -Position.Z / 1024f);
            Room = _collision.ResolveVertical(Room, Position.X / 1024f, -(Position.Y - Height / 2f) / 1024f, -Position.Z / 1024f);

            // Tırmanma bitti: sırtüstü atlama komutu çalıştıysa havadayız, kenarın üstüne çıkıldıysa ayaktayız
            if (_inAir || CurrentAnim.StateID == StateStop) _climbing = false;
        }

        private void DropFromWall()
        {
            _climbing = false;
            _inAir = true;
            _fallSpeed = 0f;
            _airSpeed = 0f;
            SetAnimation(DropAnimation, _level.Animations[DropAnimation].FrameStart);
        }

        // Önündeki duvarın üstü (kenar) yaklaşık bir tur yukarıdaysa (±256) kenarın üstüne çıkılır
        private bool CanClimbOver(out float topY)
        {
            if (!TryGetClimbWall(out float snapped, out topY)) return false;
            if (MathF.Abs(topY - (Position.Y - ClimbStep)) > 300f) return false;

            float fx = Position.X + MathF.Sin(snapped) * (Radius + 64f);
            float fz = Position.Z + MathF.Cos(snapped) * (Radius + 64f);
            if (!TryFloorAt(fx, fz, out _, out int room)) return false;
            float ceilingY = -_collision.GetCeilingHeight(room, fx / 1024f, -fz / 1024f) * 1024f;
            return ceilingY <= topY - Height; // Kenarın üstünde ayakta duracak yer
        }

        // Duvar bir tur daha yukarı devam ediyor ve başın üstünde yer var mı?
        private bool CanClimbUp()
        {
            if (!TryGetClimbWall(out _, out float topY) || topY > Position.Y - ClimbStep - 300f) return false; // Kenar yakınsa üstüne çıkılır
            int headRoom = _collision.ResolveVertical(Room, Position.X / 1024f, -(Position.Y - Height) / 1024f, -Position.Z / 1024f);
            float ceilingY = -_collision.GetCeilingHeight(headRoom, Position.X / 1024f, -Position.Z / 1024f) * 1024f;
            return ceilingY <= Position.Y - Height - 256f;
        }

        // Ayakların altında bir tur daha inecek yer var mı?
        private bool CanClimbDown()
        {
            int footRoom = _collision.ResolveVertical(Room, Position.X / 1024f, -Position.Y / 1024f, -Position.Z / 1024f);
            float floorY = -_collision.GetFloorHeight(footRoom, Position.X / 1024f, -Position.Z / 1024f) * 1024f;
            return floorY >= Position.Y + ClimbStep && IsClimbableFacing(Position.X, Position.Z, Angle);
        }

        // Yana doğru duvar aynı yönde tırmanılabilir olmaya devam ediyor mu?
        private bool CanClimbSide(float side)
        {
            float angle = Angle + side * MathF.PI / 2f;
            float x = Position.X + MathF.Sin(angle) * (SideRadius + 16f);
            float z = Position.Z + MathF.Cos(angle) * (SideRadius + 16f);
            int room = _collision.ResolvePortals(Room, x / 1024f, -z / 1024f);
            return !_collision.IsWall(room, x / 1024f, -z / 1024f) && IsClimbableFacing(x, z, Angle);
        }
    }
}
