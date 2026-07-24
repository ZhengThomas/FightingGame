using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

// Snapshot of world state at a single frame.
// Expand this as rollback needs grow (velocities, player states, health, etc.)
public struct GameState
{
    public Vector3 P1Position;
    public Vector3 P2Position;
}

public partial class MatchManager : Node
{
    public const int StateHistorySize = 60;
    GameState[] stateHistory = new GameState[StateHistorySize];
    int stateHistoryHead = 0; // index of the next slot to write

    // Records the current positions into the ring buffer.
    // Call this after all position resolution for the frame is complete.
    private void RecordGameState()
    {
        stateHistory[stateHistoryHead] = new GameState
        {
            P1Position = Player1?.Position ?? Vector3.Zero,
            P2Position = Player2?.Position ?? Vector3.Zero,
        };
        stateHistoryHead = (stateHistoryHead + 1) % StateHistorySize;
    }

    // framesAgo = 0 → most recently recorded frame, 1 → one frame before that, etc.
    public GameState GetGameState(int framesAgo)
    {
        int index = (stateHistoryHead - 1 - framesAgo + StateHistorySize) % StateHistorySize;
        return stateHistory[index];
    }

    public Vector3 GetHistoricalPosition(Player player, int framesAgo)
    {
        GameState state = GetGameState(framesAgo);
        if (player == Player1) return state.P1Position;
        if (player == Player2) return state.P2Position;
        return Vector3.Zero;
    }

    public Player Player1 { get; private set; }
    public Player Player2 { get; private set; }
    public int FrameCount { get; private set; } = 0;
	public DebugDraw DebugDraw { get; private set; }
	public float p1xLastFrame { get; private set; } = -1.5f;
	public float p2xLastFrame { get; private set; } = 1.5f;
	int hitPauseFramesRemaining = 0;
	public const int DefaultHitstopDurationLight = 4;
	public const int DefaultHitstopDurationMedium = 7;
	public const int DefaultHitstopDurationHeavy = 9;
	public bool IsInHitPause => hitPauseFramesRemaining > 0;

	// Draw order: which player's mesh should render on top where the two overlap (like GGST).
	// Defaults to Player 1; whoever lands a hit or grab most recently is brought to the front.
	// PlayerAnimator reads FrontPlayer and applies a depth-only bias in the shader (no size change).
	Player frontPlayer;
	public Player FrontPlayer => frontPlayer ?? Player1;

	// Bring a player to the front layer (draw-order only, no gameplay effect). Called when a player
	// starts an attack so the attacker — and its slash VFX — draw over the opponent, GGST-style,
	// even before the hit connects.
	public void BringToFront(Player player) => frontPlayer = player;

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

	private void ResolvePushboxCollision()
	{
		if (Player1 == null || Player2 == null) return;

		float p1Left   = Player1.Position.X + Player1.Pushbox.X - Player1.Pushbox.Width  / 2;
		float p1Right  = Player1.Position.X + Player1.Pushbox.X + Player1.Pushbox.Width  / 2;
		float p1Bottom = Player1.Position.Y + Player1.Pushbox.Y - Player1.Pushbox.Height / 2;
		float p1Top    = Player1.Position.Y + Player1.Pushbox.Y + Player1.Pushbox.Height / 2;

		float p2Left   = Player2.Position.X + Player2.Pushbox.X - Player2.Pushbox.Width  / 2;
		float p2Right  = Player2.Position.X + Player2.Pushbox.X + Player2.Pushbox.Width  / 2;
		float p2Bottom = Player2.Position.Y + Player2.Pushbox.Y - Player2.Pushbox.Height / 2;
		float p2Top    = Player2.Position.Y + Player2.Pushbox.Y + Player2.Pushbox.Height / 2;

		bool horizontalOverlap = p1Right > p2Left && p1Left < p2Right;
		bool verticalOverlap   = p1Top   > p2Bottom && p1Bottom < p2Top;

		if (horizontalOverlap && verticalOverlap)
		{
			float overlap = Player1.Position.X < Player2.Position.X 
				? p1Right - p2Left 
				: p2Right - p1Left;

			float push = Math.Min(overlap / 2, PlayerConstants.MaxPushboxCorrectionPerFrame);

			// figure out who is on which side
			bool p1IsLeft = Player1.Position.X < Player2.Position.X || (Player1.Position.X == Player2.Position.X && p1xLastFrame < p2xLastFrame);

			if (p1IsLeft)
			{
				Player1.Position = new Vector3(Player1.Position.X - push, Player1.Position.Y, Player1.Position.Z);
				Player2.Position = new Vector3(Player2.Position.X + push, Player2.Position.Y, Player2.Position.Z);
			}
			else
			{
				Player1.Position = new Vector3(Player1.Position.X + push, Player1.Position.Y, Player1.Position.Z);
				Player2.Position = new Vector3(Player2.Position.X - push, Player2.Position.Y, Player2.Position.Z);
			}
		}

		// Hard clamp, if there is a vertical overlap, we make sure that player1 and player2 never pass eachother
		// This is done by instead just freezing their x position back to what they were last frame if they do pass eachother
		
		if (verticalOverlap)
		{
			bool p1ShouldBeLeft = p1xLastFrame < p2xLastFrame;
			bool p1IsLeft = Player1.Position.X < Player2.Position.X;
			if (p1ShouldBeLeft != p1IsLeft)
			{
				Player1.Position = new Vector3(p1xLastFrame, Player1.Position.Y, Player1.Position.Z);
				Player2.Position = new Vector3(p2xLastFrame, Player2.Position.Y, Player2.Position.Z);
			}
		}
		
	}

	private void EnforceStageBoundaries()
	{
		if (Player1 == null || Player2 == null) return;

		float p1x = Player1.Position.X;
		float p2x = Player2.Position.X;

		float separation = Mathf.Abs(p1x - p2x);

		if (separation > PlayerConstants.MaxPlayerSeparation)
		{
			float halfMax = PlayerConstants.MaxPlayerSeparation / 2f;
			float MidpointLastFrame = (p1xLastFrame + p2xLastFrame) / 2;
			// clamp each player to within halfMax of the midpoint
			float p1Limit = Mathf.Clamp(p1x, MidpointLastFrame - halfMax, MidpointLastFrame + halfMax);
			float p2Limit = Mathf.Clamp(p2x, MidpointLastFrame - halfMax, MidpointLastFrame + halfMax);
			Player1.Position = new Vector3(p1Limit, Player1.Position.Y, Player1.Position.Z);
			Player2.Position = new Vector3(p2Limit, Player2.Position.Y, Player2.Position.Z);
			
		}

		// wall clamping — neither player can go past the stage edges
		float wall = PlayerConstants.MaxDistanceFromCenter;
		// yucky bandaid solution but probably wont get fixed cuz i cant think of anything
		// Theres probably an edge case where both players are in the corner at the same time,
		// But i dont think it will be a problem
		bool p1InCorner = Math.Abs(p1xLastFrame) >= wall;
		bool p2InCorner = Math.Abs(p2xLastFrame) >= wall;
		float p1Walls = p2InCorner ? wall - 0.1f : wall;
		float p2Walls = p1InCorner ? wall - 0.1f : wall;

		Player1.Position = new Vector3(Mathf.Clamp(Player1.Position.X, -p1Walls, p1Walls), Player1.Position.Y, Player1.Position.Z);
		Player2.Position = new Vector3(Mathf.Clamp(Player2.Position.X, -p2Walls, p2Walls), Player2.Position.Y, Player2.Position.Z);
	}
	public override void _PhysicsProcess(double delta)
	{
		if (debug.ShouldTick)
		{
			if (!IsInHitPause)
			{
				Player1?.Tick();
				Player2?.Tick();
				EnforceStageBoundaries(); // do it twice to ensure nothing silly happens
				ResolvePushboxCollision();
				EnforceStageBoundaries();
				ResolveHitboxCollision();
			}
			else
			{
				hitPauseFramesRemaining--;
			}

			if (Player1 != null) p1xLastFrame = Player1.Position.X;
			if (Player2 != null) p2xLastFrame = Player2.Position.X;

			FrameCount++;
			RecordGameState();
		}

		DrawDebugBoxes();
	}

	private void ResolveHitboxCollision()
	{
		if (Player1 == null || Player2 == null) return;
		CheckHit(Player1, Player2);
		CheckHit(Player2, Player1);
	}

	private void CheckHit(Player attacker, Player defender)
	{
		List<Box> hurtboxes = defender.GetCurrentHurtboxes();
		if (hurtboxes.Count == 0) return;

		int facingMult = attacker.GetFacing() == FacingDirection.Right ? 1 : -1;

		foreach (ActiveMove attack in attacker.GetActiveMoves())
		{
			if (defender.WasAlreadyHitBy(attack.Id)) continue;

			List<Box> hitboxes = attack.GetCurrentHitboxes();
			if (hitboxes.Count == 0) continue;

			foreach (Box hitbox in hitboxes)
			{
				float hLeft   = (hitbox.X * facingMult) + attacker.Position.X - hitbox.Width  / 2;
				float hRight  = (hitbox.X * facingMult) + attacker.Position.X + hitbox.Width  / 2;
				float hBottom = hitbox.Y + attacker.Position.Y - hitbox.Height / 2;
				float hTop    = hitbox.Y + attacker.Position.Y + hitbox.Height / 2;

				foreach (Box hurtbox in hurtboxes)
				{
					float dLeft   = hurtbox.X + defender.Position.X - hurtbox.Width  / 2;
					float dRight  = hurtbox.X + defender.Position.X + hurtbox.Width  / 2;
					float dBottom = hurtbox.Y + defender.Position.Y - hurtbox.Height / 2;
					float dTop    = hurtbox.Y + defender.Position.Y + hurtbox.Height / 2;

					bool hit = hRight > dLeft && hLeft < dRight && hTop > dBottom && hBottom < dTop;

					if (hit)
					{
						GD.Print($"{attacker.PlayerNumber} hit {defender.PlayerNumber}!");
						int attackDir = attacker.Position.X < defender.Position.X ? 1 : -1;

						if (attack.Data is GrabData grabData)
						{
							if(defender.InGrabbableState())
							{
								defender.GetGrabbed(grabData, attack.Id, attacker);
								attacker.RegisterGrab(defender, grabData);
								frontPlayer = attacker; // last to connect draws in front
							}
						}
						else if (attack.Data is AttackData hitData)
						{
							bool blocked = defender.TakeHit(hitData, attack.Id, attackDir, attacker);
							attacker.RegisterHit(attack.Id, blocked);
							TriggerHitPause(hitData.HitPauseDuration, hitData.Strength);
							frontPlayer = attacker; // last to connect draws in front
						}

						return;
					}
				}
			}
		}
	}

	private void DrawAttackStateIndicator(Player player)
	{
		var attack = player.CurrentMove;
		if (attack == null) return;

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
		Box indicator = new Box { X = 0, Y = 1.0f, Width = 0.4f, Height = 0.2f };
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

			foreach (Box hitbox in Player1.GetCurrentHitboxes())
				DebugDraw.DrawWorldBox(Player1, hitbox, new Color(1, 0, 0), Player1.GetFacing() == FacingDirection.Right ? 1f : -1f);
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

			foreach (Box hitbox in Player2.GetCurrentHitboxes())
				DebugDraw.DrawWorldBox(Player2, hitbox, new Color(1, 0, 0), Player2.GetFacing() == FacingDirection.Right ? 1f : -1f);
			DrawAttackStateIndicator(Player2);
		}
	}

}
