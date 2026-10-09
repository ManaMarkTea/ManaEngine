using System.Numerics;

namespace ManaVoxel.Code.Gameplay.YachtJousting;

public readonly record struct YachtInput(Vector2 Move, bool Charge, bool Block);

public sealed class YachtFighter
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public int Facing { get; set; }
    public float Health { get; set; } = 100;
    public float Guard { get; set; } = 100;
    public float Charge { get; set; }
    public float Attack { get; set; }
    public float Cooldown { get; set; }
    public bool Blocking { get; set; }
    public bool HitThisAttack { get; set; }
    public int Score { get; set; }
}

// Fixed steps keep a lance's short active window independent of rendering speed.
public sealed class YachtMatch
{
    public YachtFighter[] Boats { get; } = [new(), new()];
    public int Target { get; }
    public float Time { get; private set; }
    public float RoundWait { get; private set; } = 2;
    public int Winner { get; private set; } = -1;
    public string Message { get; private set; } = "READY YOUR LANCE";
    public int ImpactSerial { get; private set; }
    public Vector2 ImpactPosition { get; private set; }
    private float _messageTime;

    public YachtMatch(int target = 5)
    {
        Target = Math.Clamp(target, 1, 20);
        ResetRound();
    }

    private void ResetRound()
    {
        for (var i = 0; i < 2; i++)
        {
            var b = Boats[i];
            b.Position = new Vector2(i == 0 ? -11 : 11, 0);
            b.Velocity = Vector2.Zero;
            b.Facing = i == 0 ? 1 : -1;
            b.Health = b.Guard = 100;
            b.Charge = b.Attack = b.Cooldown = 0;
            b.Blocking = b.HitThisAttack = false;
        }
    }

    public YachtInput Bot(int seat)
    {
        var b = Boats[seat];
        var enemy = Boats[1 - seat];
        var delta = enemy.Position - b.Position;
        var distance = delta.Length();
        var block = enemy.Attack > 0 && distance < 10 && b.Guard > 20;
        var move = new Vector2(MathF.Sign(delta.X) * (distance < 8.2f ? 0 : 1), Math.Clamp(delta.Y, -1, 1));
        var charge = !block && b.Cooldown <= 0 && (b.Charge < (seat == 0 ? 0.65f : 0.85f) || distance > 9);
        return new YachtInput(move, charge, block);
    }

    public void Step(float dt, YachtInput player, YachtInput rival)
    {
        dt = Math.Clamp(dt, 0, 1f / 60);
        Time += dt;
        _messageTime -= dt;
        if (Winner >= 0)
        {
            return;
        }
        if (RoundWait > 0)
        {
            RoundWait -= dt;
            if (RoundWait <= 0)
            {
                ResetRound();
                Message = "JOUST!";
                _messageTime = 1;
            }
            return;
        }
        Move(Boats[0], player, dt);
        Move(Boats[1], rival, dt);
        Strike(0);
        Strike(1);
        var separation = Boats[1].Position - Boats[0].Position;
        if (separation.LengthSquared() < 64)
        {
            var normal = separation.LengthSquared() < 0.001f ? Vector2.UnitX : Vector2.Normalize(separation);
            var push = normal * (8 - separation.Length()) * 0.5f;
            Boats[0].Position -= push;
            Boats[1].Position += push;
        }
        if (Boats.Any(b => b.Health <= 0))
        {
            if (Boats.All(b => b.Health <= 0))
            {
                RoundWait = 2.5f;
                Message = "DOUBLE KNOCKOUT — JOUST AGAIN";
                return;
            }
            var victor = Boats[0].Health <= 0 ? 1 : 0;
            Boats[victor].Score++;
            Winner = Boats[victor].Score >= Target ? victor : -1;
            RoundWait = 2.5f;
            Message = Winner >= 0 ? (victor == 0 ? "CAPTAIN ASTOR WINS!" : "VISCOUNT VANDER WINS!") : (victor == 0 ? "ASTOR TAKES THE ROUND" : "VANDER TAKES THE ROUND");
        }
        else if (_messageTime <= 0)
        {
            Message = "";
        }
    }

    private static void Move(YachtFighter b, YachtInput input, float dt)
    {
        b.Cooldown = Math.Max(0, b.Cooldown - dt);
        b.Attack = Math.Max(0, b.Attack - dt);
        b.Blocking = input.Block && b.Guard > 1 && b.Attack <= 0;
        b.Guard = Math.Clamp(b.Guard + (b.Blocking ? -24 : 22) * dt, 0, 100);
        if (!b.Blocking && b.Attack <= 0 && b.Cooldown <= 0)
        {
            if (input.Charge)
            {
                b.Charge = Math.Min(1, b.Charge + dt);
            }
            else if (b.Charge > 0)
            {
                b.Attack = 0.22f;
                b.Cooldown = 0.8f;
                b.HitThisAttack = false;
                b.Velocity += new Vector2(b.Facing * (6 + b.Charge * 9), 0);
            }
        }
        if (b.Attack <= 0 && !input.Charge)
        {
            b.Charge = 0;
        }
        var movement = input.Move.LengthSquared() > 1 ? Vector2.Normalize(input.Move) : input.Move;
        if (Math.Abs(movement.X) > 0.1f && b.Attack <= 0)
        {
            b.Facing = Math.Sign(movement.X);
        }
        b.Velocity += movement * (b.Blocking ? 12 : 25) * dt;
        b.Velocity *= MathF.Exp(-2.5f * dt);
        b.Position += b.Velocity * dt;
        b.Position = Vector2.Clamp(b.Position, new Vector2(-19, -6), new Vector2(19, 6));
    }

    private void Strike(int seat)
    {
        var a = Boats[seat];
        var b = Boats[1 - seat];
        var delta = b.Position - a.Position;
        if (a.Attack <= 0 || a.HitThisAttack || delta.X * a.Facing < 0 || Math.Abs(delta.X) > 9.5f || Math.Abs(delta.Y) > 1.8f)
        {
            return;
        }
        a.HitThisAttack = true;
        var damage = 18 + a.Charge * 32;
        var blocked = b.Blocking && b.Facing == -a.Facing && b.Guard >= 30;
        if (blocked)
        {
            b.Guard -= 30;
            Message = "PARRY!";
        }
        else
        {
            b.Health = Math.Max(0, b.Health - damage);
            Message = a.Charge > 0.7f ? "CRUSHING JOUST!" : "LANCE HIT!";
        }
        b.Velocity += new Vector2(a.Facing * (blocked ? 4 : 15), 0);
        _messageTime = 0.9f;
        ImpactSerial++;
        ImpactPosition = (a.Position + b.Position) * 0.5f;
    }
}
