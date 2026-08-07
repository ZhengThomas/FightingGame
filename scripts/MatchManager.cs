using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class MatchManager : Node
{
    public const int StateHistorySize = 60;
    Snapshot[] stateHistory = new Snapshot[StateHistorySize];

    // Ring-buffer slot for a given absolute frame number. Using FrameCount as the sole source
    // of truth for indexing means a rollback that restores FrameCount automatically restores
    // where subsequent reads/writes land — no separate head counter to keep in sync.
    private static int SlotFor(int frame) => ((frame % StateHistorySize) + StateHistorySize) % StateHistorySize;

    // Pack every sim-critical field on this MatchManager into a MatchSnapshot for rollback
    // storage. Player-node references are absent — frontPlayer is stored by number.
    public MatchSnapshot CaptureMatchSnapshot() => new MatchSnapshot
    {
        FrameCount = FrameCount,
        SimFrame = SimFrame,
        HitPauseFramesRemaining = hitPauseFramesRemaining,
        P1xLastFrame = p1xLastFrame,
        P2xLastFrame = p2xLastFrame,
        FrontPlayerNumber = frontPlayerNumber,
    };

    // Overwrite every sim-critical field on this MatchManager from a MatchSnapshot.
    public void RestoreMatchSnapshot(MatchSnapshot s)
    {
        FrameCount = s.FrameCount;
        SimFrame = s.SimFrame;
        hitPauseFramesRemaining = s.HitPauseFramesRemaining;
        p1xLastFrame = s.P1xLastFrame;
        p2xLastFrame = s.P2xLastFrame;
        frontPlayerNumber = s.FrontPlayerNumber;
    }

    // Capture the full sim state (MatchManager + both players) into one struct — the unit
    // that lives in the rolling history buffer and, later, the primitive a rollback controller
    // will hand to RestoreSnapshot to rewind time.
    public Snapshot CaptureSnapshot() => new Snapshot
    {
        Match = CaptureMatchSnapshot(),
        P1 = Player1?.CaptureSnapshot() ?? default,
        P2 = Player2?.CaptureSnapshot() ?? default,
    };

    public void RestoreSnapshot(Snapshot s)
    {
        RestoreMatchSnapshot(s.Match);
        Player1?.RestoreSnapshot(s.P1);
        Player2?.RestoreSnapshot(s.P2);
    }

    // Records the current full-match snapshot into the ring buffer.
    // Called from Tick AFTER FrameCount has been incremented, so `FrameCount - 1` is the frame
    // that just finished — that's the slot we write to.
    private void RecordGameState()
    {
        stateHistory[SlotFor(FrameCount - 1)] = CaptureSnapshot();
    }

    // True if the buffer actually holds the frame `framesAgo` back — inside the ring, and not
    // before the match started.
    public bool HasGameState(int framesAgo)
        => framesAgo >= 0 && framesAgo < StateHistorySize && FrameCount - 1 - framesAgo >= 0;

    // framesAgo = 0 → the frame that most recently finished, 1 → one frame before that, etc.
    // Reaching past what the buffer holds clamps to its oldest recorded frame
    public Snapshot GetGameState(int framesAgo)
    {
        if (FrameCount == 0) return CaptureSnapshot();
        int oldest = Math.Min(StateHistorySize, FrameCount) - 1;
        return stateHistory[SlotFor(FrameCount - 1 - Math.Clamp(framesAgo, 0, oldest))];
    }

    public (int attackerX, int defenderX) GetHistoricalSimX(Player attacker, Player defender, int framesAgo)
    {
        Snapshot state = GetGameState(framesAgo);
        int ax = attacker == Player1 ? state.P1.SimX : state.P2.SimX;
        int dx = defender == Player1 ? state.P1.SimX : state.P2.SimX;
        return (ax, dx);
    }

    public Player Player1 { get; private set; }
    public Player Player2 { get; private set; }
    // Physics-frame tick number. Always advances while ShouldTick is true, INCLUDING during
    // hit pause — this is what indexes InputLog slots and Ticks-in-a-match-so-far uses like
    // attack ID generation, so those need to keep progressing even when the sim body is frozen.
    public int FrameCount { get; private set; } = 0;
    // Sim-progress tick number. Advances only on frames non hit puase frames
    public int SimFrame { get; private set; } = 0;
	public DebugDraw DebugDraw { get; private set; }
	public int p1xLastFrame { get; private set; } = PlayerConstants.ToSim(-1.5f);
	public int p2xLastFrame { get; private set; } = PlayerConstants.ToSim(1.5f);
	int hitPauseFramesRemaining = 0;
	// Per-frame record of whether the sim body was frozen. Not snapshotted — resim rewrites it.
	readonly FrameRing<bool> hitPauseHistory = new FrameRing<bool>(InputManager.BufferSize);
	public FrameRing<bool> HitPauseHistory => hitPauseHistory;
	public const int DefaultHitstopDurationLight = 4;
	public const int DefaultHitstopDurationMedium = 7;
	public const int DefaultHitstopDurationHeavy = 9;
	public bool IsInHitPause => hitPauseFramesRemaining > 0;

	// Draw order: which player's mesh should render on top where the two overlap (like GGST).
	// Defaults to Player 1; whoever lands a hit or grab most recently is brought to the front.
	// PlayerAnimator reads FrontPlayer and applies a depth-only bias in the shader (no size change).
	// Stored as player number (1/2, 0 = unset → defaults to Player1) so it snapshots by copy.
	int frontPlayerNumber = 0;
	public Player FrontPlayer => PlayerFromNumber(frontPlayerNumber) ?? Player1;

	// Bring a player to the front layer (draw-order only, no gameplay effect). Called when a player
	// starts an attack so the attacker — and its slash VFX — draw over the opponent, GGST-style,
	// even before the hit connects.
	public void BringToFront(Player player) => frontPlayerNumber = player?.PlayerNumber ?? 0;

	DebugManager debug;
	InputManager inputManager;

    public override void _Ready()
    {
		debug = GetNode<DebugManager>("/root/DebugManager");
		inputManager = GetNode<InputManager>("/root/InputManager");
        DebugDraw = GetNode<DebugDraw>("/root/ActualFighting/DebugLayer/DebugDraw");
    }

	public void TriggerHitPause(int duration, MoveType strength)
	{
		// use Default values
		if(duration == 0)
		{
			if(strength == MoveType.Light || strength == MoveType.CrouchLight)
			{
				hitPauseFramesRemaining = DefaultHitstopDurationLight;
			}
			if(strength == MoveType.Medium || strength == MoveType.CrouchMedium)
			{
				hitPauseFramesRemaining = DefaultHitstopDurationMedium;
			}
			if(strength == MoveType.Heavy || strength == MoveType.CrouchHeavy)
			{
				hitPauseFramesRemaining = DefaultHitstopDurationHeavy;
			}
		}
		else
		{
			hitPauseFramesRemaining = duration;
		}
	}

    public void RegisterPlayer(Player player, int playerNumber)
    {
        if (playerNumber == 1) Player1 = player;
        else Player2 = player;
    }

    // Get a player reference given a number
    public Player PlayerFromNumber(int number) => number switch
    {
        1 => Player1,
        2 => Player2,
        _ => null,
    };

	// Box.Width/Height are HALF-extents in sim units, so we use them directly.
	private static void BoxExtents(int originX, int originY, Box box, out int left, out int right, out int bottom, out int top)
	{
		int x = originX + box.X;
		int y = originY + box.Y;
		left = x - box.Width;
		right = x + box.Width;
		bottom = y - box.Height;
		top = y + box.Height;
	}

	// Same as BoxExtents but flips the X offset for a facing-mirrored attacker.
	private static void BoxExtentsFacing(int originX, int originY, Box box, int facingMult, out int left, out int right, out int bottom, out int top)
	{
		int x = originX + box.X * facingMult;
		int y = originY + box.Y;
		left = x - box.Width;
		right = x + box.Width;
		bottom = y - box.Height;
		top = y + box.Height;
	}

	private void ResolvePushboxCollision()
	{
		if (Player1 == null || Player2 == null) return;

		BoxExtents(Player1.SimX, Player1.SimY, Player1.Pushbox, out int p1Left, out int p1Right, out int p1Bottom, out int p1Top);
		BoxExtents(Player2.SimX, Player2.SimY, Player2.Pushbox, out int p2Left, out int p2Right, out int p2Bottom, out int p2Top);

		bool horizontalOverlap = p1Right > p2Left && p1Left < p2Right;
		bool verticalOverlap   = p1Top   > p2Bottom && p1Bottom < p2Top;

		if (horizontalOverlap && verticalOverlap)
		{
			int overlap = Player1.SimX < Player2.SimX
				? p1Right - p2Left
				: p2Right - p1Left;

			int push = Math.Min(overlap / 2, PlayerConstants.MaxPushboxCorrectionPerFrame);

			bool p1IsLeft = Player1.SimX < Player2.SimX || (Player1.SimX == Player2.SimX && p1xLastFrame < p2xLastFrame);

			if (p1IsLeft)
			{
				Player1.SimX -= push;
				Player2.SimX += push;
			}
			else
			{
				Player1.SimX += push;
				Player2.SimX -= push;
			}
			Player1.SyncVisualPosition();
			Player2.SyncVisualPosition();
		}

		if (verticalOverlap)
		{
			bool p1ShouldBeLeft = p1xLastFrame < p2xLastFrame;
			bool p1IsLeft = Player1.SimX < Player2.SimX;
			if (p1ShouldBeLeft != p1IsLeft)
			{
				Player1.SimX = p1xLastFrame;
				Player2.SimX = p2xLastFrame;
				Player1.SyncVisualPosition();
				Player2.SyncVisualPosition();
			}
		}
	}

	private void EnforceStageBoundaries()
	{
		if (Player1 == null || Player2 == null) return;

		int p1x = Player1.SimX;
		int p2x = Player2.SimX;

		int separation = Math.Abs(p1x - p2x);

		if (separation > PlayerConstants.MaxPlayerSeparation)
		{
			int halfMax = PlayerConstants.MaxPlayerSeparation / 2;
			int MidpointLastFrame = (p1xLastFrame + p2xLastFrame) / 2;
			Player1.SimX = Math.Clamp(p1x, MidpointLastFrame - halfMax, MidpointLastFrame + halfMax);
			Player2.SimX = Math.Clamp(p2x, MidpointLastFrame - halfMax, MidpointLastFrame + halfMax);
			Player1.SyncVisualPosition();
			Player2.SyncVisualPosition();
		}

		int wall = PlayerConstants.MaxDistanceFromCenter;
		bool p1InCorner = Math.Abs(p1xLastFrame) >= wall;
		bool p2InCorner = Math.Abs(p2xLastFrame) >= wall;
		int p1Walls = p2InCorner ? wall - PlayerConstants.WallCornerInset : wall;
		int p2Walls = p1InCorner ? wall - PlayerConstants.WallCornerInset : wall;

		Player1.SimX = Math.Clamp(Player1.SimX, -p1Walls, p1Walls);
		Player2.SimX = Math.Clamp(Player2.SimX, -p2Walls, p2Walls);
		Player1.SyncVisualPosition();
		Player2.SyncVisualPosition();
	}

	private void TransferCornerPushback()
	{
		if (Player1 == null || Player2 == null) return;

		int wall = PlayerConstants.MaxDistanceFromCenter;
		const int eps = 100;

		TransferCornerPushbackFor(Player1, Player2, wall, eps);
		TransferCornerPushbackFor(Player2, Player1, wall, eps);
	}

	private static void TransferCornerPushbackFor(Player defender, Player attacker, int wall, int eps)
	{
		int pb = defender.PushbackVelocityX;
		if (pb == 0) return;

		bool intoRightWall = defender.SimX >= wall - eps && pb > 0;
		bool intoLeftWall  = defender.SimX <= -wall + eps && pb < 0;
		if (!intoRightWall && !intoLeftWall) return;

		attacker.AddPushbackVelocity(-pb);
		defender.ClearPushbackVelocity();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (debug.ShouldTick) Tick();
		DrawDebugBoxes();
	}

	// Advance the simulation by exactly one tick. Safe to call multiple times in a row for rollback
	public void Tick()
	{
		// Written before the players tick so a query for the current frame reads it.
		hitPauseHistory.Set(FrameCount, IsInHitPause);

		if (!IsInHitPause)
		{
			TransferCornerPushback();
			Player1?.Tick();
			Player2?.Tick();
			EnforceStageBoundaries();
			ResolvePushboxCollision();
			EnforceStageBoundaries();
			ResolveHitboxCollision();
			// Only advance the presentation clock on frames the sim body ran — during hit pause
			// this counter freezes and any polling observer (animator, VFX) sees ticksElapsed 0.
			SimFrame++;
		}
		else
		{
			hitPauseFramesRemaining--;
		}

		if (Player1 != null) p1xLastFrame = Player1.SimX;
		if (Player2 != null) p2xLastFrame = Player2.SimX;

		FrameCount++;
		RecordGameState();
	}

	private void ResolveHitboxCollision()
	{
		if (Player1 == null || Player2 == null) return;
		CheckHit(Player1, Player2);
		CheckHit(Player2, Player1);
	}

	// One hit-check per attacker per tick. Iterates every live hitbox on the attacker; the
	// first one that overlaps a defender hurtbox lands. Whether it lands as a strike or a grab
	// is a property of the hitbox itself 
	private void CheckHit(Player attacker, Player defender)
	{
		List<Box> hurtboxes = defender.GetCurrentHurtboxes();
		if (hurtboxes.Count == 0) return;

		foreach (HitboxInstance hb in attacker.GetActiveHitboxes())
		{
			if (defender.WasAlreadyHitBy(hb.InstanceId)) continue;
			HitboxData hbData = hb.Data;
			if (hbData == null) continue;

			List<Box> boxes = Player.LookBackForBoxes(hbData.BoxesPerFrame, hb.Frame);
			if (boxes == null) continue;

			foreach (Box hitbox in boxes)
			{
				BoxExtentsFacing(attacker.SimX, attacker.SimY, hitbox, hb.FacingAtSpawn, out int hLeft, out int hRight, out int hBottom, out int hTop);
				foreach (Box hurtbox in hurtboxes)
				{
					BoxExtents(defender.SimX, defender.SimY, hurtbox, out int dLeft, out int dRight, out int dBottom, out int dTop);
					bool hit = hRight > dLeft && hLeft < dRight && hTop > dBottom && hBottom < dTop;
					if (!hit) continue;

					int attackDir = attacker.SimX < defender.SimX ? 1 : -1;

					if (hbData.IsGrab)
					{
						if (defender.InGrabbableState())
						{
							GD.Print($"{attacker.PlayerNumber} grabbed {defender.PlayerNumber}!");
							defender.GetGrabbed(hbData, hb.InstanceId, attacker);
							attacker.RegisterGrab(defender, hbData);
							frontPlayerNumber = attacker.PlayerNumber;
						}
						// else: grab whiffed on an ungrabbable defender — no state change, no
						// hitHistory entry, hitbox stays live and re-checks next tick.
					}
					else
					{
						GD.Print($"{attacker.PlayerNumber} hit {defender.PlayerNumber}!");
						bool blocked = defender.TakeHit(hbData, hb.InstanceId, attackDir, attacker);
						attacker.RegisterHitboxLanded(hb.InstanceId, blocked);
						attacker.MarkCurrentMoveLanded(blocked);
						TriggerHitPause(hbData.HitPauseDuration, hbData.Strength);
						frontPlayerNumber = attacker.PlayerNumber;
					}
					return;
				}
			}
		}
	}

	// Draw every currently-active hitbox for `player`.
	private void DrawPlayerHitboxes(Player player)
	{
		foreach (HitboxInstance hb in player.GetActiveHitboxes())
		{
			HitboxData data = hb.Data;
			if (data == null) continue;
			List<Box> boxes = Player.LookBackForBoxes(data.BoxesPerFrame, hb.Frame);
			if (boxes == null) continue;
			Color color = data.IsGrab ? new Color(1, 0.5f, 0) : new Color(1, 0, 0);
			foreach (Box box in boxes)
				DebugDraw.DrawWorldBox(player, box, color, hb.FacingAtSpawn);
		}
	}

	private void DrawAttackStateIndicator(Player player)
	{
		ActiveMoveState attack = player.CurrentMove;
		if (!attack.HasMove) return;

		int startupEnd = attack.Data.Startup;
		int activeEnd  = startupEnd + attack.Data.Active;

		Color color;
		if (attack.Frame <= startupEnd)
			color = new Color(1, 1, 0);       // yellow = startup
		else if (attack.Frame <= activeEnd)
			color = new Color(1, 0, 0);       // red = active
		else
			color = new Color(0.5f, 0, 0.5f); // purple = recovery

		// small box above the player's head
		Box indicator = new Box { X = 0, Y = 10000, Width = 2000, Height = 1000 };
		DebugDraw.DrawWorldBox(player, indicator, color);
	}

	private void DrawDebugBoxes()
	{
		if (DebugDraw == null) return;
		if (Player1 != null && Player1.ShowDebug)
		{
			DebugDraw.DrawWorldBox(Player1, Player1.Pushbox, new Color(0, 1, 0));

			Color p1HurtboxColor = new Color(0, 0, 1);
			if(Player1.IsBlockingState())
				p1HurtboxColor = new Color(1, 1, 0);
			else if(Player1.IsHurt())
				p1HurtboxColor = new Color(1, 0, 0);
			foreach (Box hurtbox in Player1.GetCurrentHurtboxes())
				DebugDraw.DrawWorldBox(Player1, hurtbox, p1HurtboxColor);

			DrawPlayerHitboxes(Player1);
			DrawAttackStateIndicator(Player1);
		}
		
		if (Player2 != null && Player2.ShowDebug)
		{
			DebugDraw.DrawWorldBox(Player2, Player2.Pushbox, new Color(0, 1, 0));

			Color p2HurtboxColor = Player2.IsHurt() ? new Color(1, 0, 0) : new Color(0, 0, 1);
			if (Player2.IsBlockingState())
				p2HurtboxColor = new Color(1, 1, 0);
			else if (Player2.IsHurt())
				p2HurtboxColor = new Color(1, 0, 0);
			foreach (Box hurtbox in Player2.GetCurrentHurtboxes())
				DebugDraw.DrawWorldBox(Player2, hurtbox, p2HurtboxColor);

			DrawPlayerHitboxes(Player2);
			DrawAttackStateIndicator(Player2);
		}
	}

}
