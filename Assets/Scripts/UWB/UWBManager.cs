using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortal.UWB
{
    public enum UWBTransportMode
    {
        Serial,
        Udp,
        Simulation
    }

    [DefaultExecutionOrder(-100)]
    public sealed class UWBManager : MonoBehaviour
    {
        private const int AnchorCount = 6;
        private const byte NoopLoopRoleTag = 2;

        private sealed class TrackedTag
        {
            public UWBTracker tracker;
            public int externalConsumers;
            public readonly AdaptiveUwbPosition response = new AdaptiveUwbPosition();
            public bool hasPose;
            public bool hasUwbPosition;
            public Vector3 latestPositionMeters;
            public Vector3 lastUwbPositionMeters;
            public Vector3 trackingVelocityMetersPerSecond;
            public float lastUwbSampleTime = -999f;
            public float lastPredictionTime = -999f;
            public float latestPoseTime = -999f;
            public bool isPredicted;
            public Vector3 pendingJumpPosition;
            public int pendingJumpFrames;
            public Vector3 lastMeasuredPosition;
            public bool hasMeasuredPosition;
            public Vector3[] positionFrameHistory;
            public int positionFrameHistoryCount;
            public int positionFrameHistoryWriteIndex;
            public float lastPositionFrameHistoryTime = -999f;
        }

        [Serializable]
        public sealed class SimulatedTagDefinition
        {
            [Min(0)] public int tagId = 1;
            [Tooltip("Initial physical X/Z position used by editor simulation.")]
            public Vector2 initialPhysicalPosition = new Vector2(3f, 2f);
            public bool autoMove;
            [Range(0f, 10f)] public float movementPhase;

            public SimulatedTagDefinition()
            {
            }

            public SimulatedTagDefinition(int tagId, Vector2 initialPhysicalPosition, float movementPhase)
            {
                this.tagId = tagId;
                this.initialPhysicalPosition = initialPhysicalPosition;
                this.movementPhase = movementPhase;
            }
        }

        [Header("Connection")]
        [SerializeField] private UWBTransportMode transportMode = UWBTransportMode.Serial;
        [SerializeField] private bool connectOnStart = true;
        [Tooltip("Reconnect automatically after the serial cable, UDP socket, or reader stops unexpectedly.")]
        [SerializeField] private bool autoReconnect = true;
        [SerializeField, Min(0.5f)] private float reconnectDelaySeconds = 2f;

        [Header("Serial")]
        [SerializeField] private string portName = "COM3";
        [SerializeField] private int baudRate = 921600;

        [Header("UDP (NoopLoop binary datagram)")]
        [SerializeField] private string udpListenAddress = "0.0.0.0";
        [SerializeField] private int udpListenPort = 9000;

        [Header("Simulation")]
        [Tooltip("Move the selected simulated tag with WASD or arrow keys in Play Mode.")]
        [SerializeField] private bool simulationKeyboardControl;
        [SerializeField, Min(0)] private int simulationKeyboardTagId = 6;
        [SerializeField, Min(0.1f)] private float simulationKeyboardSpeedMetersPerSecond = 1.8f;
        [SerializeField] private bool clampSimulationToPhysicalBounds = true;
        [SerializeField] private Vector2 simulationMinMeters = Vector2.zero;
        [SerializeField] private Vector2 simulationMaxMeters = new Vector2(6f, 4f);
        [SerializeField, Min(0f)] private float simulationAutoMoveRadiusMeters = 0.2f;
        [SerializeField, Min(0f)] private float simulationAutoMoveSpeed = 0.8f;
        [SerializeField] private SimulatedTagDefinition[] simulatedTags =
        {
            new SimulatedTagDefinition(6, new Vector2(1.2f, 2f), 0f),
            new SimulatedTagDefinition(7, new Vector2(2.4f, 2f), 1.5f),
            new SimulatedTagDefinition(8, new Vector2(3.6f, 2f), 3f),
            new SimulatedTagDefinition(9, new Vector2(4.8f, 2f), 4.5f),
            new SimulatedTagDefinition(10, new Vector2(1.8f, 3f), 6f),
            new SimulatedTagDefinition(11, new Vector2(4.2f, 3f), 7.5f)
        };

        [Header("Scene Anchors")]
        [Tooltip("Physical anchor positions placed in the Unity scene, matched by index to Anchor Device Ids. If UWBConfig.json has UWBAnchorPositions set, those override these Transforms' positions at Start.")]
        [SerializeField] private Transform[] anchorSceneObjects = new Transform[AnchorCount];
        [SerializeField] private int[] anchorDeviceIds = { 0, 1, 2, 3, 4, 5 };
        [SerializeField, Range(3, AnchorCount)] private int trackingAnchorCount = 4;
        [SerializeField] private float anchorOnlineDistanceThresholdMeters = 0.01f;
        [Tooltip("Trust the tag's own onboard position instead of trilaterating from the Scene Anchors above. Turn this off once the anchors are physically measured and placed in the scene.")]
        [SerializeField] private bool useDevicePositionFirst = true;

        [Header("Axis Conversion")]
        [Tooltip("How the tag's raw onboard position (NoopLoop protocol X/Y/Z) maps onto Unity X/Y/Z. Overridden by UWBConfig.json's axisConversion if a UWBConfigManager is present. Only applies to the device-reported position, not to scene-anchor trilateration (that's already in Unity space).")]
        [SerializeField] private Fortal.Configuration.UWBAxisConversion axisConversion = new Fortal.Configuration.UWBAxisConversion();

        [Tooltip("Meters added to the device position after axis conversion. Used to shift the tracker origin into the play area - e.g. with rawYTo=\"-z\", set Z to the field depth so a flipped axis mirrors back into 0..depth instead of going negative. Overridden by UWBConfig.json's UWBInputOffset.")]
        [SerializeField] private Vector3 inputOffset = Vector3.zero;

        [Tooltip("Per-axis multiplier applied after the offset, so Unity 0,0 stays fixed while the walking distance on each axis is stretched or shrunk. Lower X if the player runs ahead of the tag further from the center. Overridden by UWBConfig.json's UWBInputAxisScale.")]
        [SerializeField] private Vector3 inputAxisScale = Vector3.one;

        [Tooltip("Multiplier applied to calibrated UWB positions before they are reported to Players.")]
        [SerializeField, Min(0f)] private float metersToWorldScale = 1f;

        [Header("Tracking")]
        [SerializeField] private float maxPoseAgeSeconds = 1f;
        [Tooltip("How long a tag may continue along its last measured walking direction when UWB frames are temporarily missing.")]
        [SerializeField, Range(0f, 1f)] private float maxTrackingPredictionSeconds = 0.35f;
        [SerializeField] private bool rejectTrackingPositionJumps = true;
        [SerializeField, Range(0.1f, 2f)] private float maxTrackingPositionJumpMeters = 0.75f;
        [SerializeField, Range(0.5f, 5f)] private float maxTrackingPredictionSpeedMetersPerSecond = 2.5f;
        [SerializeField, Range(0.05f, 0.35f)] private float maxTrackingRecoveryStepMeters = 0.15f;
        [SerializeField, Range(0.5f, 3f)] private float maxUwbMotionSpeedMetersPerSecond = 1.8f;

        [Header("Position Safety")]
        [SerializeField] private bool rejectSolvedPositionOutsideAnchorBounds = true;
        [SerializeField] private float solvedPositionBoundsMarginMeters = 1f;
        [SerializeField] private bool holdLastPositionWhenRejected = true;

        [Header("Player Plane Lock")]
        [Tooltip("Ignore the UWB vertical coordinate and keep every tag on one horizontal gameplay plane.")]
        [SerializeField] private bool lockPlayersToFixedHeight = true;
        [SerializeField] private float fixedPlayerHeightY;

        [Header("Position Smoothing")]
        [SerializeField] private bool smoothTagPosition = true;
        [SerializeField, Range(0f, 0.1f)] private float trackingPositionDeadZoneMeters = 0.08f;
        [SerializeField] private bool averageUwbPositionFrames = true;
        [SerializeField, Range(3, 4)] private int uwbPositionAverageFrameCount = 4;
        [SerializeField, Range(0f, 1f)] private float movingLatestFrameBlend = 0.7f;
        [SerializeField, Range(0.01f, 1f)] private float stationaryPositionLerp = 0.25f;
        [SerializeField, Range(0.01f, 1f)] private float movingPositionLerp = 0.85f;

        [Header("State")]
        [SerializeField] private bool isConnected;
        [SerializeField] private bool isReceivingProtocolFrames;
        [SerializeField] private bool isReceivingFrames;
        [SerializeField] private float lastProtocolFrameAgeSeconds = 999f;
        [SerializeField] private float lastFrameAgeSeconds = 999f;
        [SerializeField] private int receivedFrameCount;
        [SerializeField] private string status = "Idle";

        private readonly Dictionary<int, TrackedTag> tags = new Dictionary<int, TrackedTag>();
        private readonly object poseLock = new object();
        private NoopLoopFrameParser parser = new NoopLoopFrameParser();
        private readonly Queue<TimedPose> pendingPoses = new Queue<TimedPose>();
        private struct TimedPose
        {
            public NoopLoopPose pose;
            public double receivedAt;
        }

        private static double ReceiveClock => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        private double protocolReceivedAt = -999;
        private double usableReceivedAt = -999;
        private double processingSampleTime;
        private float processingSampleAge;

        [Header("Low latency tracking")]
        [SerializeField] private bool adaptiveTracking = true;
        [SerializeField, Range(0f, 0.1f)] private float adaptiveDeadband = 0.03f;
        [SerializeField, Range(0.02f, 0.5f)] private float maxQueuedPoseAgeSeconds = 0.1f;
        public bool UsesAdaptiveTracking => adaptiveTracking && smoothTagPosition;
        public int DroppedStaleFrames { get; private set; }
        public int DroppedOverflowFrames { get; private set; }
        private readonly List<TimedPose> posesForMainThread = new List<TimedPose>(32);
        private readonly Dictionary<int, Vector2> simulatedPhysicalPositions = new Dictionary<int, Vector2>();
        private readonly Vector3[] trilaterationPoints = new Vector3[AnchorCount];
        private readonly float[] trilaterationDistances = new float[AnchorCount];
        private readonly float[,] leastSquaresAta = new float[3, 3];
        private readonly float[] leastSquaresAtb = new float[3];

        private NoopLoopSerialPort serialPort;
        private UdpClient udpClient;
        private Thread readThread;
        private volatile bool keepReading;
        private string threadStatus = "Idle";
        private bool connectionRequested;
        private float nextReconnectTime = -1f;
        private int parsedFrameCount;
        private int usableFrameCount;
        private int observedParsedFrameCount;
        private int observedUsableFrameCount;
        private float lastProtocolFrameTime = -999f;
        private float lastUsableFrameTime = -999f;
        private string lastLoggedConnectionError = string.Empty;

        public bool IsConnected => isConnected;
        public bool IsSimulationMode => transportMode == UWBTransportMode.Simulation;
        public bool IsSerialMode => transportMode == UWBTransportMode.Serial;
        public bool IsReceivingProtocolFrames => isReceivingProtocolFrames;
        public bool IsReceivingFrames => isReceivingFrames;
        public float LastProtocolFrameAgeSeconds => lastProtocolFrameAgeSeconds;
        public float LastFrameAgeSeconds => lastFrameAgeSeconds;
        public int ReceivedFrameCount => receivedFrameCount;
        public string Status => status;

        private void Start()
        {
            Fortal.Configuration.UWBConfigData config = Fortal.Configuration.UWBConfigManager.GetConfig();
            if (config != null)
            {
                portName = config.UWBSerialPort;
                if (config.UWBBaudRate > 0)
                {
                    baudRate = config.UWBBaudRate;
                }

                if (config.axisConversion != null)
                {
                    axisConversion = config.axisConversion;
                }

                inputOffset = config.UWBInputOffset;
                inputAxisScale = config.UWBInputAxisScale;
                metersToWorldScale = Mathf.Max(0f, config.metersToWorldScale);
                ApplyTrackingSettingsFromConfig(config.tracking);
                ApplyAnchorPositionsFromConfig(config);
            }

            if (axisConversion == null)
            {
                axisConversion = new Fortal.Configuration.UWBAxisConversion();
            }

            axisConversion.Validate();
            ValidateRuntimeSetup();

            if (connectOnStart)
            {
                Connect();
            }
        }

        private void ApplyTrackingSettingsFromConfig(Fortal.Configuration.UWBTrackingSettings tracking)
        {
            if (tracking == null)
            {
                return;
            }

            tracking.Validate();
            maxPoseAgeSeconds = tracking.maxPoseAgeSeconds;
            maxTrackingPredictionSeconds = tracking.maxTrackingPredictionSeconds;
            rejectTrackingPositionJumps = tracking.rejectTrackingPositionJumps;
            maxTrackingPositionJumpMeters = tracking.maxTrackingPositionJumpMeters;
            maxTrackingPredictionSpeedMetersPerSecond = tracking.maxTrackingPredictionSpeedMetersPerSecond;
            maxTrackingRecoveryStepMeters = tracking.maxTrackingRecoveryStepMeters;
            maxUwbMotionSpeedMetersPerSecond = tracking.maxUwbMotionSpeedMetersPerSecond;
            smoothTagPosition = tracking.smoothTagPosition;
            trackingPositionDeadZoneMeters = tracking.trackingPositionDeadZoneMeters;
            averageUwbPositionFrames = tracking.averageUwbPositionFrames;
            uwbPositionAverageFrameCount = tracking.uwbPositionAverageFrameCount;
            movingLatestFrameBlend = tracking.movingLatestFrameBlend;
            stationaryPositionLerp = tracking.stationaryPositionLerp;
            movingPositionLerp = tracking.movingPositionLerp;
        }

        private void ApplyAnchorPositionsFromConfig(Fortal.Configuration.UWBConfigData config)
        {
            if (config.UWBAnchorPositions == null || anchorSceneObjects == null)
            {
                return;
            }

            if (!HasUsableAnchorGeometry(config.UWBAnchorPositions, config.UWBAnchorPositions.Length))
            {
                if (!useDevicePositionFirst)
                {
                    Debug.LogWarning(
                        "[UWBManager] UWBAnchorPositions is still empty or invalid. " +
                        "Keeping scene anchor positions and falling back to device positions when solving is unavailable.",
                        this);
                }

                return;
            }

            int count = Mathf.Min(config.UWBAnchorPositions.Length, anchorSceneObjects.Length);
            for (int i = 0; i < count; i++)
            {
                if (anchorSceneObjects[i] != null)
                {
                    anchorSceneObjects[i].position = config.UWBAnchorPositions[i];
                }
            }
        }

        private void OnDestroy()
        {
            connectionRequested = false;
            Disconnect();
        }

        [ContextMenu("Connect")]
        public void Connect()
        {
            connectionRequested = true;
            if (keepReading && (transportMode == UWBTransportMode.Simulation || readThread != null))
            {
                return;
            }

            if (readThread != null)
            {
                CloseTransport();
            }

            TryOpenTransport();
        }

        [ContextMenu("Reset Simulation Positions")]
        public void ResetSimulationPositions()
        {
            simulatedPhysicalPositions.Clear();
        }

        private void TryOpenTransport()
        {
            if (readThread != null)
            {
                return;
            }

            ResetTracking();
            parser = new NoopLoopFrameParser();
            lock (poseLock)
            {
                protocolReceivedAt = usableReceivedAt = -999;
                pendingPoses.Clear();
                observedParsedFrameCount = parsedFrameCount;
                observedUsableFrameCount = usableFrameCount;
                lastProtocolFrameTime = -999f;
                lastUsableFrameTime = -999f;
            }

            if (transportMode == UWBTransportMode.Simulation)
            {
                simulatedPhysicalPositions.Clear();
                keepReading = true;
                threadStatus = "Simulation ready";
                status = threadStatus;
                nextReconnectTime = -1f;
                lastLoggedConnectionError = string.Empty;
                return;
            }

            try
            {
                keepReading = true;
                if (transportMode == UWBTransportMode.Serial)
                {
                    serialPort = new NoopLoopSerialPort(portName, baudRate);
                    serialPort.Open();
                    threadStatus = $"Open {portName} @ {baudRate}";
                    Debug.Log($"[UWBManager] Serial opened: {portName} @ {baudRate}", this);
                }
                else
                {
                    IPAddress listenIp = IPAddress.Parse(udpListenAddress);
                    udpClient = new UdpClient(new IPEndPoint(listenIp, udpListenPort));
                    udpClient.Client.ReceiveTimeout = 100;
                    threadStatus = $"UDP {udpListenAddress}:{udpListenPort}";
                    Debug.Log($"[UWBManager] UDP listening: {udpListenAddress}:{udpListenPort}", this);
                }

                NoopLoopSerialPort openedPort = serialPort;
                UdpClient openedUdp = udpClient;
                readThread = new Thread(() => ReadLoop(openedPort, openedUdp))
                {
                    IsBackground = true,
                    Name = "NoopLoop UWB Reader"
                };
                readThread.Start();
                nextReconnectTime = -1f;
                lastLoggedConnectionError = string.Empty;
            }
            catch (Exception ex)
            {
                threadStatus = $"Open failed: {ex.Message}";
                if (!string.Equals(lastLoggedConnectionError, ex.Message, StringComparison.Ordinal))
                {
                    lastLoggedConnectionError = ex.Message;
                    Debug.LogError($"[UWBManager] Connection failed: {ex.Message}", this);
                }
                CloseTransport();
                ScheduleReconnect();
            }
        }

        [ContextMenu("Disconnect")]
        public void Disconnect()
        {
            connectionRequested = false;
            nextReconnectTime = -1f;
            CloseTransport();
            threadStatus = "Disconnected";
            status = threadStatus;
        }

        private void CloseTransport()
        {
            keepReading = false;
            ResetTracking();
            lock (poseLock) { pendingPoses.Clear(); }
            isConnected = isReceivingProtocolFrames = isReceivingFrames = false;

            // Close UDP ก่อน Join เพื่อปลุก Receive() ที่กำลัง block อยู่
            if (udpClient != null)
            {
                udpClient.Close();
                udpClient = null;
            }

            if (readThread != null)
            {
                if (!readThread.Join(200))
                {
                    // The reader owns its handle until it has actually stopped.
                    isConnected = isReceivingProtocolFrames = isReceivingFrames = false;
                    return;
                }
                readThread = null;
            }

            if (serialPort != null)
            {
                try
                {
                    if (serialPort.IsOpen)
                    {
                        serialPort.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UWBManager] Serial close warning: {ex.Message}", this);
                }

                serialPort = null;
            }

            lock (poseLock) { pendingPoses.Clear(); }
            isConnected = false;
            isReceivingProtocolFrames = false;
            isReceivingFrames = false;
        }

        private void ScheduleReconnect()
        {
            if (connectionRequested && autoReconnect)
            {
                nextReconnectTime = Time.unscaledTime + Mathf.Max(0.5f, reconnectDelaySeconds);
            }
        }

        private void ReadLoop(NoopLoopSerialPort openedPort, UdpClient openedUdp)
        {
            try
            {
                while (keepReading)
                {
                    try
                    {
                        if (transportMode == UWBTransportMode.Serial)
                        {
                            int value = openedPort.ReadByte();
                            if (value >= 0 && keepReading)
                            {
                                PushProtocolByte((byte)value);
                            }
                        }
                        else
                        {
                            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                            byte[] datagram = openedUdp.Receive(ref sender);
                            for (int i = 0; i < datagram.Length && keepReading; i++)
                            {
                                PushProtocolByte(datagram[i]);
                            }
                        }
                    }
                    catch (TimeoutException)
                    {
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                    }
                    catch (SocketException) when (!keepReading)
                    {
                    }
                    catch (ObjectDisposedException) when (!keepReading)
                    {
                    }
                    catch (Exception ex)
                    {
                        lock (poseLock)
                        {
                            threadStatus = $"Read stopped: {ex.Message}";
                        }

                        keepReading = false;
                    }
                }
            }
            finally
            {
                openedPort?.Dispose();
                keepReading = false;
                lock (poseLock) { pendingPoses.Clear(); }
            }
        }

        private void ResetTracking()
        {
            foreach (TrackedTag tag in tags.Values)
            {
                tag.hasPose = tag.hasUwbPosition = tag.hasMeasuredPosition = false;
                tag.pendingJumpFrames = tag.positionFrameHistoryCount = tag.positionFrameHistoryWriteIndex = 0;
                tag.trackingVelocityMetersPerSecond = Vector3.zero;
                tag.response.Reset();
                tag.tracker?.SetOffline();
            }
        }

        private void PushProtocolByte(byte value)
        {
            if (!parser.Push(value, out NoopLoopPose pose))
            {
                return;
            }

            bool usable = pose.FrameType != "AnchorFrame0-NoTag";
            lock (poseLock)
            {
                parsedFrameCount++;
                protocolReceivedAt = ReceiveClock;
                if (usable)
                {
                    usableFrameCount++;
                    usableReceivedAt = protocolReceivedAt;

                    if (pendingPoses.Count >= 256)
                    {
                        pendingPoses.Dequeue();
                        DroppedOverflowFrames++;
                    }

                    pendingPoses.Enqueue(new TimedPose { pose = pose, receivedAt = protocolReceivedAt });
                }

                threadStatus = usable ? $"{pose.FrameType} OK: T{pose.Id}" : "AnchorFrame0 OK, waiting for Tag";
            }
        }

        public void AddTag(UWBTracker tracker)
        {
            if (tracker == null)
            {
                return;
            }

            if (!tags.TryGetValue(tracker.tagId, out TrackedTag entry))
            {
                entry = new TrackedTag();
                tags[tracker.tagId] = entry;
            }

            entry.tracker = tracker;
        }


        public void RegisterTag(int tagId)
        {
            if (!tags.TryGetValue(tagId, out TrackedTag entry))
            {
                entry = new TrackedTag();
                tags[tagId] = entry;
            }

            entry.externalConsumers++;
        }

        public void UnregisterTag(int tagId)
        {
            if (!tags.TryGetValue(tagId, out TrackedTag entry))
            {
                return;
            }

            entry.externalConsumers = Mathf.Max(0, entry.externalConsumers - 1);
            if (entry.externalConsumers == 0 && entry.tracker == null)
            {
                tags.Remove(tagId);
            }
        }

        public void RemoveTag(UWBTracker tracker)
        {
            if (tracker == null)
            {
                return;
            }

            if (tags.TryGetValue(tracker.tagId, out TrackedTag entry) && entry.tracker == tracker)
            {
                entry.tracker = null;
                if (entry.externalConsumers == 0)
                {
                    tags.Remove(tracker.tagId);
                }
            }
        }

        public bool TryGetTagPosition(int tagId, out Vector3 positionMeters, out float ageSeconds)
        {
            positionMeters = default;
            ageSeconds = 999f;

            if (!tags.TryGetValue(tagId, out TrackedTag entry) || !entry.hasPose)
            {
                return false;
            }

            ageSeconds = Mathf.Max(0f, Time.unscaledTime - entry.latestPoseTime);
            positionMeters = entry.latestPositionMeters;
            return ageSeconds <= maxPoseAgeSeconds;
        }

        public bool IsTagOnline(int tagId)
        {
            return TryGetTagPosition(tagId, out _, out _);
        }

        public bool TryGetArenaPosition2D(int tagId, out Vector2 arenaPosition, out float ageSeconds)
        {
            arenaPosition = default;
            if (!TryGetTagPosition(tagId, out Vector3 positionMeters, out ageSeconds))
            {
                return false;
            }

            // The configured UWB coordinate frame is already the Unity world frame.
            // Keep the raw calibrated position so Players can move outside the map.
            arenaPosition = new Vector2(positionMeters.x, positionMeters.z);
            return true;
        }

        public bool SetSimulatedArenaPosition2D(int tagId, Vector2 arenaPosition)
        {
            if (transportMode != UWBTransportMode.Simulation)
            {
                return false;
            }

            simulatedPhysicalPositions[tagId] = ClampSimulatedPhysicalPosition(arenaPosition);
            return true;
        }

        private void Update()
        {
            if (connectionRequested && readThread != null && !keepReading)
            {
                CloseTransport();
                ScheduleReconnect();
            }

            if (connectionRequested && autoReconnect && readThread == null &&
                nextReconnectTime >= 0f && Time.unscaledTime >= nextReconnectTime)
            {
                TryOpenTransport();
            }

            if (transportMode == UWBTransportMode.Simulation)
            {
                UpdateSimulation();
                return;
            }

            posesForMainThread.Clear();
            lock (poseLock)
            {
                while (pendingPoses.Count > 0)
                {
                    TimedPose queued = pendingPoses.Dequeue();
                    if (ReceiveClock - queued.receivedAt > maxQueuedPoseAgeSeconds)
                    {
                        DroppedStaleFrames++;
                        continue;
                    }
                    posesForMainThread.Add(queued);
                }

                isConnected = keepReading &&
                    ((transportMode == UWBTransportMode.Serial && serialPort != null && serialPort.IsOpen) ||
                     (transportMode == UWBTransportMode.Udp && udpClient != null));

                if (observedParsedFrameCount != parsedFrameCount)
                {
                    observedParsedFrameCount = parsedFrameCount;
                    lastProtocolFrameTime = Time.unscaledTime - (float)(ReceiveClock - protocolReceivedAt);
                }

                if (observedUsableFrameCount != usableFrameCount)
                {
                    observedUsableFrameCount = usableFrameCount;
                    lastUsableFrameTime = Time.unscaledTime - (float)(ReceiveClock - usableReceivedAt);
                }

                receivedFrameCount = parsedFrameCount;
                status = threadStatus;
            }

            lastProtocolFrameAgeSeconds = lastProtocolFrameTime > -900f
                ? Mathf.Max(0f, Time.unscaledTime - lastProtocolFrameTime)
                : 999f;
            lastFrameAgeSeconds = lastUsableFrameTime > -900f
                ? Mathf.Max(0f, Time.unscaledTime - lastUsableFrameTime)
                : 999f;
            isReceivingProtocolFrames = isConnected && lastProtocolFrameAgeSeconds <= maxPoseAgeSeconds;
            isReceivingFrames = isConnected && lastFrameAgeSeconds <= maxPoseAgeSeconds;

            for (int i = 0; i < posesForMainThread.Count; i++)
            {
                TimedPose timed = posesForMainThread[i];
                processingSampleTime = timed.receivedAt;
                processingSampleAge = Mathf.Max(0f, (float)(ReceiveClock - timed.receivedAt));
                if (processingSampleAge <= maxQueuedPoseAgeSeconds)
                {
                    ApplyPoseToRegisteredTags(timed.pose);
                }
            }

            PredictTagsDuringSignalGap();
        }

        private void UpdateSimulation()
        {
            isConnected = connectionRequested && keepReading;
            if (!isConnected)
            {
                isReceivingProtocolFrames = false;
                isReceivingFrames = false;
                lastProtocolFrameAgeSeconds = 999f;
                lastFrameAgeSeconds = 999f;
                status = threadStatus;
                return;
            }

            float now = Time.unscaledTime;
            Vector2 keyboardInput = ReadSimulationKeyboardInput();
            int simulatedTagCount = 0;

            foreach (KeyValuePair<int, TrackedTag> pair in tags)
            {
                int tagId = pair.Key;
                TrackedTag entry = pair.Value;
                SimulatedTagDefinition definition = GetSimulatedTagDefinition(tagId);

                if (!simulatedPhysicalPositions.TryGetValue(tagId, out Vector2 basePosition))
                {
                    basePosition = definition != null
                        ? definition.initialPhysicalPosition
                        : GetFallbackSimulatedPosition(tagId);
                }

                if (simulationKeyboardControl && tagId == simulationKeyboardTagId && keyboardInput.sqrMagnitude > 0f)
                {
                    basePosition += keyboardInput * simulationKeyboardSpeedMetersPerSecond * Time.unscaledDeltaTime;
                }

                basePosition = ClampSimulatedPhysicalPosition(basePosition);
                simulatedPhysicalPositions[tagId] = basePosition;

                Vector2 sampledPosition = basePosition;
                if (definition != null && definition.autoMove && simulationAutoMoveRadiusMeters > 0f)
                {
                    float phase = (now * simulationAutoMoveSpeed) + definition.movementPhase;
                    sampledPosition += new Vector2(Mathf.Cos(phase), Mathf.Sin(phase)) * simulationAutoMoveRadiusMeters;
                    sampledPosition = ClampSimulatedPhysicalPosition(sampledPosition);
                }

                Vector3 positionMeters = ApplyPlayerHeightPolicy(
                    new Vector3(sampledPosition.x, fixedPlayerHeightY, sampledPosition.y));

                float sampleDt = entry.hasUwbPosition
                    ? Mathf.Max(0.001f, now - entry.lastUwbSampleTime)
                    : Mathf.Max(0.001f, Time.unscaledDeltaTime);
                entry.trackingVelocityMetersPerSecond = entry.hasUwbPosition
                    ? (positionMeters - entry.lastUwbPositionMeters) / sampleDt
                    : Vector3.zero;
                entry.lastUwbPositionMeters = positionMeters;
                entry.lastUwbSampleTime = now;
                entry.hasUwbPosition = true;
                entry.hasPose = true;
                entry.latestPositionMeters = positionMeters;
                entry.latestPoseTime = now;
                entry.lastPredictionTime = now;
                entry.isPredicted = false;
                entry.tracker?.ApplyTrackedPosition(positionMeters, 0f);
                simulatedTagCount++;
            }

            parsedFrameCount++;
            receivedFrameCount = parsedFrameCount;
            lastProtocolFrameTime = now;
            lastProtocolFrameAgeSeconds = 0f;
            isReceivingProtocolFrames = true;

            if (simulatedTagCount > 0)
            {
                usableFrameCount++;
                lastUsableFrameTime = now;
                lastFrameAgeSeconds = 0f;
                isReceivingFrames = true;
                threadStatus = $"Simulation OK: {simulatedTagCount} tags";
            }
            else
            {
                lastFrameAgeSeconds = 999f;
                isReceivingFrames = false;
                threadStatus = "Simulation ready, waiting for players";
            }

            status = threadStatus;
        }

        private Vector2 ReadSimulationKeyboardInput()
        {
            if (!simulationKeyboardControl || Keyboard.current == null)
            {
                return Vector2.zero;
            }

            Keyboard keyboard = Keyboard.current;
            float x = 0f;
            float y = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                x -= 1f;
            }
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                x += 1f;
            }
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                y -= 1f;
            }
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                y += 1f;
            }

            Vector2 input = new Vector2(x, y);
            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        private SimulatedTagDefinition GetSimulatedTagDefinition(int tagId)
        {
            if (simulatedTags == null)
            {
                return null;
            }

            for (int i = 0; i < simulatedTags.Length; i++)
            {
                if (simulatedTags[i] != null && simulatedTags[i].tagId == tagId)
                {
                    return simulatedTags[i];
                }
            }

            return null;
        }

        private Vector2 GetFallbackSimulatedPosition(int tagId)
        {
            float minX = Mathf.Min(simulationMinMeters.x, simulationMaxMeters.x);
            float maxX = Mathf.Max(simulationMinMeters.x, simulationMaxMeters.x);
            float minY = Mathf.Min(simulationMinMeters.y, simulationMaxMeters.y);
            float maxY = Mathf.Max(simulationMinMeters.y, simulationMaxMeters.y);
            // Spread up to six unlisted tags across the area without two landing on the same spot.
            float x01 = ((Mathf.Abs(tagId) % 6) + 1f) / 7f;
            return new Vector2(Mathf.Lerp(minX, maxX, x01), Mathf.Lerp(minY, maxY, 0.5f));
        }

        private Vector2 ClampSimulatedPhysicalPosition(Vector2 position)
        {
            if (!clampSimulationToPhysicalBounds)
            {
                return position;
            }

            position.x = Mathf.Clamp(
                position.x,
                Mathf.Min(simulationMinMeters.x, simulationMaxMeters.x),
                Mathf.Max(simulationMinMeters.x, simulationMaxMeters.x));
            position.y = Mathf.Clamp(
                position.y,
                Mathf.Min(simulationMinMeters.y, simulationMaxMeters.y),
                Mathf.Max(simulationMinMeters.y, simulationMaxMeters.y));
            return position;
        }

        private void ValidateRuntimeSetup()
        {
            if (transportMode == UWBTransportMode.Serial && string.IsNullOrWhiteSpace(portName))
            {
                Debug.LogError("[UWBManager] Serial mode requires UWBSerialPort in UWBConfig.json.", this);
            }

            if (!useDevicePositionFirst && !HasUsableSceneAnchorGeometry())
            {
                Debug.LogWarning(
                    "[UWBManager] Trilateration is enabled but fewer than three non-collinear scene anchors are assigned. " +
                    "Tracking will safely fall back to the tag's onboard position until anchors are configured.",
                    this);
            }
        }

        private bool HasUsableSceneAnchorGeometry()
        {
            if (anchorSceneObjects == null)
            {
                return false;
            }

            Vector3[] positions = new Vector3[anchorSceneObjects.Length];
            int count = 0;
            for (int i = 0; i < anchorSceneObjects.Length; i++)
            {
                if (anchorSceneObjects[i] != null)
                {
                    positions[count++] = anchorSceneObjects[i].position;
                }
            }

            return HasUsableAnchorGeometry(positions, count);
        }

        private static bool HasUsableAnchorGeometry(Vector3[] positions, int count)
        {
            if (positions == null || count < 3)
            {
                return false;
            }

            int safeCount = Mathf.Min(count, positions.Length);
            for (int a = 0; a < safeCount - 2; a++)
            {
                for (int b = a + 1; b < safeCount - 1; b++)
                {
                    Vector3 ab = positions[b] - positions[a];
                    if (ab.sqrMagnitude < 0.000001f)
                    {
                        continue;
                    }

                    for (int c = b + 1; c < safeCount; c++)
                    {
                        Vector3 ac = positions[c] - positions[a];
                        if (Vector3.Cross(ab, ac).sqrMagnitude > 0.000001f)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // Emitted on the main thread before tracking iteration, allowing a consumer to register a new tag.
        public event Action<int> TagDetected;

        private void ApplyPoseToRegisteredTags(NoopLoopPose framePose)
        {
            if (framePose.Role == NoopLoopRoleTag && framePose.FrameType != "AnchorFrame0-NoTag")
                TagDetected?.Invoke(framePose.Id);
            if (framePose.NodeIds != null && framePose.NodeRoles != null)
                for (int i = 0; i < framePose.NodeIds.Length && i < framePose.NodeRoles.Length; i++)
                    if (framePose.NodeRoles[i] == NoopLoopRoleTag) TagDetected?.Invoke(framePose.NodeIds[i]);
            foreach (KeyValuePair<int, TrackedTag> pair in tags)
            {
                if (TryGetPoseForTag(framePose, pair.Key, out NoopLoopPose tagPose))
                {
                    UpdateTrackedTagFromPose(pair.Value, tagPose);
                }
            }
        }

        private void PredictTagsDuringSignalGap()
        {
            float now = Time.unscaledTime;
            foreach (KeyValuePair<int, TrackedTag> pair in tags)
            {
                TrackedTag entry = pair.Value;
                if (!entry.hasPose || !entry.hasUwbPosition)
                {
                    continue;
                }

                float age = now - entry.latestPoseTime;
                if (age > maxPoseAgeSeconds)
                {
                    entry.tracker?.SetOffline();
                    continue;
                }

                if (UsesAdaptiveTracking)
                {
                    entry.latestPositionMeters = ApplyPlayerHeightPolicy(entry.response.Present(now));
                    entry.tracker?.ApplyTrackedPosition(entry.latestPositionMeters, age);
                    continue;
                }

                if (maxTrackingPredictionSeconds <= 0f || age <= 0.01f || age > maxTrackingPredictionSeconds)
                {
                    if (age > maxTrackingPredictionSeconds)
                    {
                        entry.trackingVelocityMetersPerSecond = Vector3.zero;
                    }
                    continue;
                }

                float previousPredictionAge = Mathf.Max(0f, entry.lastPredictionTime - entry.latestPoseTime);
                float predictionAge = Mathf.Min(age, maxTrackingPredictionSeconds);
                float dt = predictionAge - previousPredictionAge;
                if (dt <= 0f)
                {
                    continue;
                }

                entry.latestPositionMeters += entry.trackingVelocityMetersPerSecond * dt;
                entry.latestPositionMeters = ApplyPlayerHeightPolicy(entry.latestPositionMeters);
                entry.lastPredictionTime = now;
                entry.isPredicted = true;

                entry.tracker?.ApplyTrackedPosition(entry.latestPositionMeters, age);
            }
        }

        private bool TryGetPoseForTag(NoopLoopPose framePose, int tagId, out NoopLoopPose tagPose)
        {
            tagPose = default;
            if (framePose.Id == tagId && framePose.FrameType != "AnchorFrame0-NoTag")
            {
                tagPose = framePose;
                return true;
            }

            if (framePose.NodeIds == null || framePose.NodeRoles == null || framePose.NodeAnchorDistancesMeters == null)
            {
                return false;
            }

            for (int i = 0; i < framePose.NodeIds.Length; i++)
            {
                if (framePose.NodeIds[i] != tagId)
                {
                    continue;
                }

                if (i < framePose.NodeRoles.Length && framePose.NodeRoles[i] != NoopLoopRoleTag)
                {
                    continue;
                }

                Vector3 position = Vector3.zero;
                if (framePose.NodePositionsMeters != null && i < framePose.NodePositionsMeters.Length)
                {
                    position = framePose.NodePositionsMeters[i];
                }

                float[] distances = i < framePose.NodeAnchorDistancesMeters.Length ? framePose.NodeAnchorDistancesMeters[i] : null;
                tagPose = new NoopLoopPose((byte)Mathf.Clamp(tagId, 0, 255), NoopLoopRoleTag, "AnchorFrame0-NodeTag", position, distances, framePose.Voltage);
                return true;
            }

            return false;
        }

        private void UpdateTrackedTagFromPose(TrackedTag target, NoopLoopPose tagPose)
        {
            Vector3 raw = tagPose.PositionMeters;
            if (!float.IsFinite(raw.x) || !float.IsFinite(raw.y) || !float.IsFinite(raw.z))
            {
                return;
            }
            // A returning tag starts from its new calibrated position, not old velocity/history.
            if (Time.unscaledTime - target.latestPoseTime > 0.25f)
            {
                target.hasPose = target.hasUwbPosition = target.hasMeasuredPosition = false;
                target.pendingJumpFrames = target.positionFrameHistoryCount = 0;
                target.response.Reset();
            }
            Vector3 resolvedPosition = ResolveTagPosition(tagPose, target);
            if (!float.IsFinite(resolvedPosition.x) || !float.IsFinite(resolvedPosition.y) || !float.IsFinite(resolvedPosition.z))
            {
                return;
            }

            float now = Time.unscaledTime - processingSampleAge;
            if (target.hasUwbPosition)
            {
                float sampleDt = Mathf.Max(0.001f, now - target.lastUwbSampleTime);
                Vector3 measuredVelocity = (resolvedPosition - target.lastUwbPositionMeters) / sampleDt;
                float measuredSpeed = measuredVelocity.magnitude;
                if (measuredSpeed > maxTrackingPredictionSpeedMetersPerSecond)
                {
                    measuredVelocity = measuredVelocity.normalized * maxTrackingPredictionSpeedMetersPerSecond;
                }

                target.trackingVelocityMetersPerSecond = Vector3.Lerp(target.trackingVelocityMetersPerSecond, measuredVelocity, 0.65f);
            }
            else
            {
                target.trackingVelocityMetersPerSecond = Vector3.zero;
            }

            target.lastUwbPositionMeters = resolvedPosition;
            target.lastUwbSampleTime = now;
            target.hasUwbPosition = true;
            target.lastPredictionTime = now;
            target.isPredicted = false;

            target.hasPose = true;
            target.latestPositionMeters = resolvedPosition;
            target.latestPoseTime = now;

            if (!UsesAdaptiveTracking)
            {
                target.tracker?.ApplyTrackedPosition(resolvedPosition, processingSampleAge);
            }
        }

        private Vector3 ResolveTagPosition(NoopLoopPose pose, TrackedTag target)
        {

            Vector3 devicePosition =
                Vector3.Scale(axisConversion.Apply(pose.PositionMeters) + inputOffset, inputAxisScale) * metersToWorldScale;
            Vector3 position = devicePosition;
            bool fromSolver = false;
            if (!useDevicePositionFirst && TryTrilaterateFromSceneAnchors(pose, out Vector3 solved))
            {
                position = solved;
                fromSolver = true;
            }

            if (fromSolver && !IsSolvedPositionAccepted(position, target, out _))
            {
                if (holdLastPositionWhenRejected && target.hasPose)
                {
                    return ApplyPlayerHeightPolicy(target.latestPositionMeters);
                }

                return ApplyPlayerHeightPolicy(devicePosition);
            }

            if (rejectTrackingPositionJumps && target.hasPose && target.hasUwbPosition)
            {
                float jump = Vector3.Distance(target.lastUwbPositionMeters, position);
                if (jump > maxTrackingPositionJumpMeters)
                {
                    bool sameJump = target.pendingJumpFrames > 0 &&
                        Vector3.Distance(target.pendingJumpPosition, position) <= maxTrackingPositionJumpMeters * 0.5f;
                    target.pendingJumpPosition = position;
                    target.pendingJumpFrames = sameJump ? Mathf.Min(target.pendingJumpFrames + 1, 3) : 1;

                    if (target.pendingJumpFrames < 3)
                    {
                        return ApplyPlayerHeightPolicy(target.latestPositionMeters);
                    }

                    position = Vector3.MoveTowards(target.lastUwbPositionMeters, position, maxTrackingRecoveryStepMeters);
                }
                else
                {
                    target.pendingJumpFrames = 0;
                }
            }
            else
            {
                target.pendingJumpFrames = 0;
            }

            position = ApplyPlayerHeightPolicy(position);
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
            {
                return position;
            }
            position = UsesAdaptiveTracking
                ? target.response.Sample(position, processingSampleTime, Time.unscaledTime, adaptiveDeadband)
                : SmoothTagPositionValue(position, target);
            return ApplyPlayerHeightPolicy(position);
        }

        private Vector3 ApplyPlayerHeightPolicy(Vector3 position)
        {
            if (lockPlayersToFixedHeight)
            {
                position.y = fixedPlayerHeightY;
            }

            return position;
        }

        private Vector3 SmoothTagPositionValue(Vector3 position, TrackedTag target)
        {
            if (!smoothTagPosition)
            {
                return position;
            }

            Vector3 averagedPosition = AddPositionFrameAndGetAverage(position, target);
            if (!target.hasPose)
            {
                target.lastMeasuredPosition = averagedPosition;
                target.hasMeasuredPosition = true;
                return position;
            }

            Vector3 previous = target.latestPositionMeters;
            float dt = target.latestPoseTime > 0f
                ? Mathf.Max(Time.unscaledTime - target.latestPoseTime, 0.001f)
                : Mathf.Max(Time.unscaledDeltaTime, 0.001f);

            float measuredSpeed = 0f;
            if (target.hasMeasuredPosition)
            {
                measuredSpeed = Vector3.Distance(averagedPosition, target.lastMeasuredPosition) / dt;
            }

            target.lastMeasuredPosition = averagedPosition;
            target.hasMeasuredPosition = true;

            float speed01 = Mathf.InverseLerp(0.12f, 0.7f, measuredSpeed);
            float latestFrameBlend = Mathf.Clamp01(movingLatestFrameBlend) * speed01;
            Vector3 filteredTarget = Vector3.Lerp(averagedPosition, position, latestFrameBlend);
            Vector3 rawDelta = filteredTarget - previous;

            float deadZone = Mathf.Max(0f, trackingPositionDeadZoneMeters) * Mathf.Lerp(1f, 0.15f, speed01);
            if (deadZone > 0f && rawDelta.sqrMagnitude <= deadZone * deadZone)
            {
                return previous;
            }

            float maxStep = Mathf.Max(maxTrackingRecoveryStepMeters, maxUwbMotionSpeedMetersPerSecond * dt * 1.1f);
            if (rawDelta.magnitude > maxStep)
            {
                filteredTarget = previous + rawDelta.normalized * maxStep;
            }

            float alpha = Mathf.Lerp(Mathf.Clamp01(stationaryPositionLerp), Mathf.Clamp01(movingPositionLerp), speed01);
            return Vector3.Lerp(previous, filteredTarget, alpha);
        }

        private Vector3 AddPositionFrameAndGetAverage(Vector3 position, TrackedTag target)
        {
            if (!averageUwbPositionFrames)
            {
                return position;
            }

            int frameCount = Mathf.Clamp(uwbPositionAverageFrameCount, 3, 4);
            float now = Time.unscaledTime;
            bool historyExpired = now - target.lastPositionFrameHistoryTime > Mathf.Max(0.5f, maxPoseAgeSeconds);
            if (target.positionFrameHistory == null || target.positionFrameHistory.Length != frameCount || historyExpired)
            {
                target.positionFrameHistory = new Vector3[frameCount];
                target.positionFrameHistoryCount = 0;
                target.positionFrameHistoryWriteIndex = 0;
            }

            target.positionFrameHistory[target.positionFrameHistoryWriteIndex] = position;
            target.positionFrameHistoryWriteIndex = (target.positionFrameHistoryWriteIndex + 1) % frameCount;
            target.positionFrameHistoryCount = Mathf.Min(target.positionFrameHistoryCount + 1, frameCount);
            target.lastPositionFrameHistoryTime = now;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < target.positionFrameHistoryCount; i++)
            {
                sum += target.positionFrameHistory[i];
            }

            return sum / Mathf.Max(1, target.positionFrameHistoryCount);
        }

        private bool IsSolvedPositionAccepted(Vector3 position, TrackedTag target, out string reason)
        {
            reason = string.Empty;
            if (rejectSolvedPositionOutsideAnchorBounds && TryGetAnchorBounds(out Bounds bounds))
            {
                bounds.Expand(solvedPositionBoundsMarginMeters * 2f);
                bool insideX = position.x >= bounds.min.x && position.x <= bounds.max.x;
                bool insideZ = position.z >= bounds.min.z && position.z <= bounds.max.z;
                if (!insideX || !insideZ)
                {
                    reason = "outside anchor bounds";
                    return false;
                }
            }

            return true;
        }

        private bool TryGetAnchorPosition(int anchorIndex, out Vector3 positionMeters)
        {
            positionMeters = default;
            if (anchorIndex < 0 || anchorIndex >= AnchorCount)
            {
                return false;
            }

            if (anchorSceneObjects != null && anchorIndex < anchorSceneObjects.Length && anchorSceneObjects[anchorIndex] != null)
            {
                positionMeters = anchorSceneObjects[anchorIndex].position;
                return true;
            }

            return false;
        }

        private int GetAnchorDeviceId(int anchorIndex)
        {
            if (anchorDeviceIds == null || anchorIndex < 0 || anchorIndex >= anchorDeviceIds.Length)
            {
                return anchorIndex;
            }

            return anchorDeviceIds[anchorIndex];
        }

        private bool TryGetAnchorBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasAny = false;
            for (int i = 0; i < AnchorCount; i++)
            {
                if (!TryGetAnchorPosition(i, out Vector3 anchorPosition))
                {
                    continue;
                }

                if (!hasAny)
                {
                    bounds = new Bounds(anchorPosition, Vector3.zero);
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(anchorPosition);
                }
            }

            return hasAny;
        }

        private bool TryTrilaterateFromSceneAnchors(NoopLoopPose pose, out Vector3 position)
        {
            position = default;
            if (pose.AnchorDistancesMeters == null)
            {
                return false;
            }

            int anchorLimit = Mathf.Clamp(trackingAnchorCount, 3, AnchorCount);
            int count = 0;
            for (int i = 0; i < anchorLimit; i++)
            {
                if (!TryGetAnchorPosition(i, out Vector3 anchorPosition))
                {
                    continue;
                }

                int deviceId = GetAnchorDeviceId(i);
                if (deviceId < 0 || deviceId >= pose.AnchorDistancesMeters.Length)
                {
                    continue;
                }

                float distance = pose.AnchorDistancesMeters[deviceId];
                if (distance <= anchorOnlineDistanceThresholdMeters)
                {
                    continue;
                }

                trilaterationPoints[count] = anchorPosition;
                trilaterationDistances[count] = distance;
                count++;
            }

            if (count < 3)
            {
                return false;
            }

            return UwbTrilaterationSolver.TrySolve(
                trilaterationDistances,
                trilaterationPoints,
                count,
                leastSquaresAta,
                leastSquaresAtb,
                out position);
        }

    }
}
