using System.Collections.Generic;
using Fortal.Display;
using Fortal.Players;
using Fortal.UWB;
using UnityEngine;

namespace Deepspace
{
    // Where the game's players stand, read from the players UWBPlayerSpawner made. The game plays in slots (slot 0 is
    // Player 1): a player takes the next free slot when they first come online, until LockRoster, and the player pads can
    // reorder them.
    // Everything reads world positions, so this works the same for UWB tags and for Simulation.
    public sealed class FloorPlayerInput
    {
        public const int MaxSlots = UWBPlayerSpawner.MaximumPlayers;
        private const int FreeSlot = -1;

        private readonly UWBPlayerSpawner spawner;
        private readonly UWBManager manager;
        private readonly List<int> playerIdBySlot = new List<int>(MaxSlots);
        private bool locked;

        public FloorPlayerInput(UWBPlayerSpawner spawner, UWBManager manager)
        {
            this.spawner = spawner;
            this.manager = manager;
        }

        public bool IsSimulation => spawner != null && spawner.IsStandaloneSimulationMode;

        // True when positions can arrive at all, so the game counts whoever stands on the floor.
        public bool IsLive => IsSimulation || (manager != null && manager.IsConnected);

        // How many slots are taken, counting anyone who has dropped out since.
        public int SlotCount
        {
            get
            {
                AddNewcomers();
                return playerIdBySlot.Count;
            }
        }

        public bool TryGetPlayer(int slot, out UWBPlayerController player)
        {
            player = null;
            if (spawner == null || slot < 0) return false;

            AddNewcomers();
            if (slot >= playerIdBySlot.Count || playerIdBySlot[slot] == FreeSlot) return false;
            return spawner.TryGetPlayer(playerIdBySlot[slot], out player) && IsOnline(player);
        }

        public bool TryGetWorld(int slot, out Vector3 world)
        {
            bool found = TryGetPlayer(slot, out UWBPlayerController player);
            world = found ? player.transform.position : default;
            return found;
        }

        // The player's point in space's local coordinates, even past the edge of its rect.
        public bool TryGetLocalUnclamped(int slot, RectTransform space, out Vector2 local)
        {
            local = default;
            if (space == null || !TryGetWorld(slot, out Vector3 world)) return false;

            local = space.InverseTransformPoint(world);
            return true;
        }

        // The same point held inside the rect: a tag just past the edge still counts as standing at that edge.
        public bool TryGetLocal(int slot, RectTransform space, out Vector2 local)
        {
            if (!TryGetLocalUnclamped(slot, space, out local)) return false;

            Rect rect = space.rect;
            local = new Vector2(Mathf.Clamp(local.x, rect.xMin, rect.xMax), Mathf.Clamp(local.y, rect.yMin, rect.yMax));
            return true;
        }

        // 0..1 across space, y = 0 at the bottom.
        public Vector2? Normalized(int slot, RectTransform space)
        {
            if (!TryGetLocal(slot, space, out Vector2 local)) return null;

            Rect rect = space.rect;
            return new Vector2(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x), Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
        }

        // Who holds this slot, online or not: someone away for a moment keeps their place.
        public int PlayerIdForSlot(int slot)
        {
            AddNewcomers();
            return slot >= 0 && slot < playerIdBySlot.Count ? playerIdBySlot[slot] : FreeSlot;
        }

        // Puts these player ids in slots 0, 1, ...; a value below 1 leaves that slot free.
        public void SetOrder(IReadOnlyList<int> playerIds)
        {
            playerIdBySlot.Clear();
            for (int i = 0; i < playerIds.Count && i < MaxSlots; i++)
            {
                playerIdBySlot.Add(playerIds[i] > 0 ? playerIds[i] : FreeSlot);
            }
        }

        // A new game: slots go out again in the order players are online, and anyone may join.
        public void ResetOrder()
        {
            playerIdBySlot.Clear();
            if (!locked) return;

            locked = false;
            if (spawner == null) return;

            var everyone = new List<int>(MaxSlots);
            for (int id = 1; id <= MaxSlots; id++) everyone.Add(id);
            spawner.LockRoster(everyone);
        }

        // The game has gone into the stage: nobody new joins until ResetOrder. Those already in keep their slots, and come
        // back to them whenever their tag does.
        public void LockRoster()
        {
            AddNewcomers();
            locked = true;
            if (spawner != null) spawner.LockRoster(playerIdBySlot);
        }

        // Paints the player's floor marker, so the marker matches the colour the game shows for that slot.
        public void TintMarker(int slot, Color color)
        {
            if (!TryGetPlayer(slot, out UWBPlayerController player)) return;

            TagCircle circle = player.GetComponentInChildren<TagCircle>(true);
            if (circle != null) circle.color = color;
        }

        // Simulation only: places the player by hand. Vector3 is a world position; its height is ignored.
        public void MoveInSimulation(int slot, Vector3 world)
        {
            if (IsSimulation && TryGetPlayer(slot, out UWBPlayerController player))
            {
                player.SetStandaloneWorldPosition(new Vector2(world.x, world.z));
            }
        }

        private void AddNewcomers()
        {
            if (locked || spawner == null) return;

            IReadOnlyList<UWBPlayerController> spawned = spawner.SpawnedPlayers;
            for (int i = 0; i < spawned.Count && playerIdBySlot.Count < MaxSlots; i++)
            {
                UWBPlayerController player = spawned[i];
                if (player != null && IsOnline(player) && !playerIdBySlot.Contains(player.PlayerId))
                {
                    playerIdBySlot.Add(player.PlayerId);
                }
            }
        }

        private bool IsOnline(UWBPlayerController player)
        {
            return player != null && player.isActiveAndEnabled && (IsSimulation || player.IsTracking);
        }
    }
}
