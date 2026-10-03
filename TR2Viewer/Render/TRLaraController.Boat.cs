using OpenTK.Mathematics;

namespace TR2Viewer.Render
{
    // Lara'nın teknede oturması: tekneye düşünce/bölüme teknede başlayınca oturur, Space + sol/sağ ile atlayarak iner.
    // Oyun bu animasyonları Lara'nın kendi modelinden değil "Lara tekne animasyonları" nesnesinden (model 11) oynatır;
    // numaralar o nesnenin ilk animasyonuna göredir (oyunun tekne kodundaki sabitler, veriyle doğrulandı).
    public partial class TRLaraController
    {
        private const int LaraBoatModelId = 11;
        private const int BoatStillAnim = 1;       // Oturarak bekleme (döngü)
        private const int BoatJumpLeftAnim = 5;    // Sola atlayarak inme
        private const int BoatGetOnJumpAnim = 6;   // Tekneye atlayıp oturma
        private const int BoatJumpRightAnim = 7;   // Sağa atlayarak inme
        private const int BoatStateStill = 1;
        private const int BoatCooldownTicks = 30;  // İndikten sonra hemen tekrar binilmesin

        private int _boatAnimBase = -1;            // Tekne animasyonlarının başladığı indeks (yoksa -1)
        private bool _inBoat;
        private int _boatCooldown;

        private void InitBoat()
        {
            foreach (var model in _level.Models)
            {
                if (model.ID == LaraBoatModelId && model.NumMeshes == Entity.Model.NumMeshes &&
                    model.Animation + BoatJumpRightAnim < _level.Animations.Length)
                {
                    _boatAnimBase = model.Animation;
                }
            }
        }

        // Lara (x, z) noktasında bir teknenin üstündeyse o tekneyi döndürür
        private bool TryGetBoatAt(float x, float z, out TRSolidBox boat)
        {
            foreach (var box in _solids)
            {
                if (box.IsBoat && box.Contains(x, z))
                {
                    boat = box;
                    return true;
                }
            }
            boat = default;
            return false;
        }

        // Tekneye bin: Lara teknenin konumuna ve yönüne oturur (tekne animasyonları teknenin merkezine göredir)
        private bool TryEnterBoat(float x, float z)
        {
            if (_boatAnimBase < 0 || _inBoat || _boatCooldown > 0 || !TryGetBoatAt(x, z, out var boat)) return false;

            _inBoat = true;
            _inAir = false;
            _landed = false;
            _fallSpeed = 0f;
            _airSpeed = 0f;
            Position = new Vector3(boat.X, boat.Y, boat.Z);
            Angle += WrapAngle(boat.Angle - Angle);
            SetAnimation(_boatAnimBase + BoatGetOnJumpAnim, _level.Animations[_boatAnimBase + BoatGetOnJumpAnim].FrameStart);
            return true;
        }

        private void TickBoat(LaraInput input)
        {
            var anim = CurrentAnim;
            int index = Entity.Animation - _boatAnimBase;

            // Otururken Space + sol/sağ: o yöne atlayarak in
            if (anim.StateID == BoatStateStill && input.Jump && (input.Left || input.Right))
            {
                int jump = _boatAnimBase + (input.Left ? BoatJumpLeftAnim : BoatJumpRightAnim);
                SetAnimation(jump, _level.Animations[jump].FrameStart);
                return;
            }

            // İnme animasyonu bitti: oyundaki gibi o yöne dön, 360 birim yana çık ve küçük bir sıçrayışla düş
            bool jumpingOff = index is BoatJumpLeftAnim or BoatJumpRightAnim;
            if (jumpingOff && _frame + 1 > anim.FrameEnd)
            {
                LeaveBoat(index == BoatJumpLeftAnim);
                return;
            }

            Animate(anim, anim.StateID, anim.StateID);
        }

        private void LeaveBoat(bool left)
        {
            Angle += left ? -MathF.PI / 2f : MathF.PI / 2f;
            Position.X += MathF.Sin(Angle) * 360f;
            Position.Z += MathF.Cos(Angle) * 360f;
            if (TryGetBoatAt(Position.X, Position.Z, out var boat)) Position.Y = boat.TopY; // Hâlâ teknenin üstünde: güverteden sıçra
            Room = _collision.ResolvePortals(Room, Position.X / 1024f, -Position.Z / 1024f);

            _inBoat = false;
            _boatCooldown = BoatCooldownTicks;
            _inAir = true;
            _fallSpeed = -40f;
            _airSpeed = 20f;
            _airMoveAngle = Angle;
            SetAnimation(FallAnimation, _level.Animations[FallAnimation].FrameStart);
        }
    }
}
