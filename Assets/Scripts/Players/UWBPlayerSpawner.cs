using System.Collections.Generic;
using Fortal.UWB;
using UnityEngine;

namespace Fortal.Players
{
    // Spawns playerCount players, each with no UWB tag at first: a tag that connects goes to the
    // first player still without one, whatever its number, first come first served, and stays with
    // that player for the session. Once every player has a tag, any further tag is turned away.
    public sealed class UWBPlayerSpawner : MonoBehaviour
    {
        public const int MaximumPlayers = 6;

        [Header("Player Template")]
        [Tooltip("Player prefab with UWBPlayerController, Rigidbody and SphereCollider.")]
        [SerializeField] private UWBPlayerController playerPrefab;
        [SerializeField] private Transform playerParent;
        [SerializeField] private bool spawnOnStart = true;

        [Header("Player Size")]
        [Tooltip("Overall scale of every Player, including marker and collider.")]
        [SerializeField, Min(0.05f)] private float playerScale = 0.462f;

        [Header("Players")]
        [Tooltip("Players in the game (Player 1 up). Each takes the next UWB tag that connects; tags connecting after that many are turned away.")]
        [SerializeField, Range(1, MaximumPlayers)] private int playerCount = 4;
        [Tooltip("Floor X/Z of Player 1, 2, ... before the first UWB frame, and the spawn point in standalone Simulation (Vector2.y is world Z).")]
        [SerializeField] private Vector2[] startPositions =
        {
            new Vector2(-3.3f, -1.65f),
            new Vector2(-1.1f, -1.65f),
            new Vector2(1.1f, -1.65f),
            new Vector2(3.3f, -1.65f),
            new Vector2(-4.4f, -1.65f),
            new Vector2(4.4f, -1.65f)
        };

        [Header("Simulation")]
        [Tooltip("Number of simulated players (1-6). Applies to standalone Simulation only.")]
        [SerializeField, Range(1, MaximumPlayers)] private int simulationPlayerCount = 4;

        [Header("Serial Presence")]
        [Tooltip("In Serial mode, disable each Player until its UWB tag is receiving fresh data. The tag stays registered so the Player can come back.")]
        [SerializeField] private bool disableOfflinePlayersInSerial = true;

        private readonly List<UWBPlayerController> spawnedPlayers = new List<UWBPlayerController>();
        // Tags turned away (logged once each).
        private readonly HashSet<int> refusedTags = new HashSet<int>();
        // Once the game has started, only these players may come online; null while anyone may join.
        private HashSet<int> roster;
        private UWBManager uwbManager;
        private bool standaloneSimulationMode;

        public IReadOnlyList<UWBPlayerController> SpawnedPlayers => spawnedPlayers;

        // True when UWBManager is set to Simulation; players are then placed and dragged by hand.
        public bool IsStandaloneSimulationMode => standaloneSimulationMode;

        private void Awake()
        {
            UWBManager manager = FindAnyObjectByType<UWBManager>();
            standaloneSimulationMode = manager != null && manager.IsSimulationMode;
            if (!standaloneSimulationMode)
            {
                return;
            }

            // Simulation only selects the mode here, so the UWB simulation pipeline is
            // switched off to keep it from overwriting hand-placed players.
            manager.enabled = false;
        }

        private void Start()
        {
            uwbManager = standaloneSimulationMode ? null : FindAnyObjectByType<UWBManager>();
            if (uwbManager != null) uwbManager.TagDetected += OnTagDetected;
            if (spawnOnStart)
            {
                SpawnPlayers();
            }
        }

        private void Update()
        {
            RefreshSerialPlayerPresence();
        }

        private void OnDestroy()
        {
            if (uwbManager == null)
            {
                return;
            }

            uwbManager.TagDetected -= OnTagDetected;
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                if (spawnedPlayers[i] != null && spawnedPlayers[i].TagId != UWBPlayerController.NoTag)
                {
                    uwbManager.UnregisterTag(spawnedPlayers[i].TagId);
                }
            }
        }

        // A tag was heard. One no player has yet goes to the first player still without a tag; with
        // none left it is turned away.
        private void OnTagDetected(int tagId)
        {
            if (tagId < 0 || refusedTags.Contains(tagId)) return;

            UWBPlayerController waiting = null;
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                UWBPlayerController player = spawnedPlayers[i];
                if (player == null) continue;
                if (player.TagId == tagId) return;
                if (waiting == null && player.TagId == UWBPlayerController.NoTag) waiting = player;
            }

            if (waiting == null)
            {
                refusedTags.Add(tagId);
                Debug.Log($"[UWBPlayerSpawner] Tag {tagId} turned away: all {spawnedPlayers.Count} players already have a tag", this);
                return;
            }

            waiting.SetTagId(tagId);
            uwbManager.RegisterTag(tagId);
            Debug.Log($"[UWBPlayerSpawner] Tag {tagId} joins as Player {waiting.PlayerId}", this);
        }

        [ContextMenu("Spawn Players")]
        public void SpawnPlayers()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UWBPlayerSpawner] Spawn Players works in Play Mode only.", this);
                return;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("[UWBPlayerSpawner] Assign the player prefab before spawning.", this);
                return;
            }

            ClearSpawnedPlayers();
            Transform targetParent = playerParent != null ? playerParent : transform;
            int count = Mathf.Clamp(standaloneSimulationMode ? simulationPlayerCount : playerCount, 1, MaximumPlayers);
            for (int i = 0; i < count; i++)
            {
                spawnedPlayers.Add(SpawnPlayer(i + 1, StartPosition(i), targetParent));
            }
        }

        private Vector2 StartPosition(int index)
        {
            return startPositions != null && index < startPositions.Length ? startPositions[index] : Vector2.zero;
        }

        // Closes the game to newcomers: from now on only these players show when their tag is online
        // (they may still drop out and come back). Others stay hidden even if their tag connects.
        public void LockRoster(IEnumerable<int> playerIds)
        {
            roster = new HashSet<int>(playerIds);
            Debug.Log($"[UWBPlayerSpawner] Players locked in: {string.Join(", ", roster)}", this);
        }

        public bool TryGetPlayer(int playerId, out UWBPlayerController player)
        {
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                if (spawnedPlayers[i] != null && spawnedPlayers[i].PlayerId == playerId)
                {
                    player = spawnedPlayers[i];
                    return true;
                }
            }

            player = null;
            return false;
        }

        // A player starts with no tag; the spawner registers the tag it is given (OnTagDetected) and
        // keeps that registration, so a disabled offline player still receives frames.
        private UWBPlayerController SpawnPlayer(int playerId, Vector2 startPosition, Transform targetParent)
        {
            UWBPlayerController controller = Instantiate(playerPrefab, targetParent);
            controller.gameObject.SetActive(false);
            controller.SetUwbTrackingEnabled(!standaloneSimulationMode);
            controller.Configure(playerId, UWBPlayerController.NoTag);
            controller.SetStandaloneWorldPosition(startPosition);
            controller.SetPlayerScale(playerScale);
            controller.gameObject.SetActive(true);
            return controller;
        }

        private void ClearSpawnedPlayers()
        {
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                if (spawnedPlayers[i] == null)
                {
                    continue;
                }

                if (spawnedPlayers[i].TagId != UWBPlayerController.NoTag) uwbManager?.UnregisterTag(spawnedPlayers[i].TagId);
                spawnedPlayers[i].gameObject.SetActive(false);
                Destroy(spawnedPlayers[i].gameObject);
            }

            spawnedPlayers.Clear();
            refusedTags.Clear();
        }

        private void RefreshSerialPlayerPresence()
        {
            if (standaloneSimulationMode || uwbManager == null || !disableOfflinePlayersInSerial || !uwbManager.IsSerialMode)
            {
                return;
            }

            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                UWBPlayerController player = spawnedPlayers[i];
                if (player == null)
                {
                    continue;
                }

                bool online = uwbManager.IsTagOnline(player.TagId);
                bool allowed = roster == null || roster.Contains(player.PlayerId);
                bool show = online && allowed;
                if (player.gameObject.activeSelf != show)
                {
                    player.gameObject.SetActive(show);
                    if (show && roster == null) Debug.Log($"[UWBPlayerSpawner] Player {player.PlayerId} (tag {player.TagId}) connected", this);
                }
            }
        }
    }
}
