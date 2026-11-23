using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using VRPortalToolkit;
using VRPortalToolkit.Portables;

public class DesktopPortalDemo : MonoBehaviour
{
    public Portal entrance;
    public Portal exit;
    public Material ballMaterial;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private int playerTeleports;
    private bool smoke;
    private readonly List<string> errors = new List<string>();
    private string output;

    private void Awake()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        Application.targetFrameRate = 60;
        var portable = GetComponent<Portable>();
        portable.postTeleport = new UnityEvent<Portal>();
        portable.postTeleport.AddListener(p => playerTeleports++);
        Application.logMessageReceived += Log;
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-portal-smoke-test");
        smoke = index >= 0 && index + 1 < args.Length;
        if (smoke) output = Path.GetFullPath(args[index + 1]);
    }

    private IEnumerator Start()
    {
        if (smoke) yield return SmokeTest();
    }

    private void Update()
    {
        if (smoke) return;
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null) return;
        Vector3 direction = Vector3.zero;
        if (keyboard.wKey.isPressed) direction += Vector3.forward;
        if (keyboard.sKey.isPressed) direction += Vector3.back;
        if (keyboard.aKey.isPressed) direction += Vector3.left;
        if (keyboard.dKey.isPressed) direction += Vector3.right;
        if (keyboard.eKey.isPressed) direction += Vector3.up;
        if (keyboard.qKey.isPressed) direction += Vector3.down;
        float speed = keyboard.leftShiftKey.isPressed ? 5 : 2.5f;
        transform.position += transform.TransformDirection(direction.normalized) * speed * Time.deltaTime;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue() * 0.12f;
            transform.Rotate(Vector3.up, delta.x, Space.World);
            transform.Rotate(Vector3.right, -delta.y, Space.Self);
        }
        if (keyboard.rKey.wasPressedThisFrame)
            transform.SetPositionAndRotation(startPosition, startRotation);
        if (keyboard.spaceKey.wasPressedThisFrame) LaunchBall();
        if (keyboard.escapeKey.wasPressedThisFrame) Application.Quit();
    }

    private Rigidbody LaunchBall()
    {
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Portable physics ball";
        ball.transform.position = transform.position + transform.forward * 0.5f;
        ball.transform.localScale = Vector3.one * 0.25f;
        ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;
        var body = ball.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.velocity = transform.forward * 4;
        ball.AddComponent<Portable>().origin = ball.transform;
        Destroy(ball, 15);
        return body;
    }

    private IEnumerator SmokeTest()
    {
        Directory.CreateDirectory(output);
        for (int i = 0; i < 90; i++) yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
        bool portalShowsDestination = ContainsDestinationColor(frame);
        File.WriteAllBytes(Path.Combine(output, "portal-before.png"), frame.EncodeToPNG());
        Destroy(frame);
        yield return new WaitForSeconds(1);
        int ballTeleports = 0;
        Vector3 approach = Vector3.ProjectOnPlane(entrance.localAnchor.position - startPosition, Vector3.up).normalized;
        Vector3 approachStart = entrance.localAnchor.position - approach;
        Rigidbody ball = LaunchBall();
        ball.position = approachStart;
        ball.velocity = approach * 2;
        var portableBall = ball.GetComponent<Portable>();
        portableBall.postTeleport = new UnityEvent<Portal>();
        portableBall.postTeleport.AddListener(p => ballTeleports++);
        transform.SetPositionAndRotation(approachStart, Quaternion.LookRotation(approach));
        yield return new WaitForFixedUpdate();
        for (int i = 0; i < 120 && playerTeleports == 0; i++)
        {
            transform.position += approach * 0.04f;
            yield return new WaitForFixedUpdate();
        }
        if (playerTeleports > 0) transform.position += transform.forward;
        yield return new WaitForSeconds(1);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "portal-after.png"));
        yield return new WaitForSeconds(1);
        bool linked = entrance.connected == exit && exit.connected == entrance && entrance.usesTeleport;
        bool moved = Vector3.Distance(transform.position, exit.transform.position) < 3
            && Vector3.Dot(transform.forward, exit.transform.forward) > 0.9f
            && Vector3.Dot(transform.position - exit.transform.position, exit.transform.forward) > 0.1f;
        bool ballReachedExit = ball && Mathf.Abs(ball.position.x - exit.transform.position.x) < 2
            && Vector3.Dot(ball.position - exit.transform.position, exit.transform.forward) > 0.1f;
        bool passed = portalShowsDestination && linked && playerTeleports > 0 && moved && ballTeleports > 0 && ballReachedExit && errors.Count == 0;
        var result = new SmokeResult
        {
            passed = passed, scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            linkedPortalPair = linked, portalShowsDestination = portalShowsDestination, playerTeleportCount = playerTeleports, physicsBallTeleportCount = ballTeleports,
            playerReachedExit = moved, physicsBallReachedExit = ballReachedExit, frameCount = Time.frameCount, errors = errors.ToArray()
        };
        File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonUtility.ToJson(result, true));
        Debug.Log("VRPORTAL_SMOKE_RESULT " + JsonUtility.ToJson(result));
        Application.Quit(passed ? 0 : 1);
    }

    private static bool ContainsDestinationColor(Texture2D image)
    {
        Color32[] pixels = image.GetPixels32();
        int orangePixels = 0;
        for (int y = image.height * 38 / 100; y < image.height * 62 / 100; y++)
            for (int x = image.width * 43 / 100; x < image.width * 57 / 100; x++)
            {
                Color32 pixel = pixels[y * image.width + x];
                if (pixel.r > 80 && pixel.r > pixel.g * 1.25f && pixel.r > pixel.b * 1.5f)
                    if (++orangePixels >= 64) return true;
            }
        return false;
    }

    private void Log(string condition, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(condition);
    }

    private void OnDestroy() { Application.logMessageReceived -= Log; }

    private void OnGUI()
    {
        GUI.Box(new Rect(14, 14, 600, 82), "VRPortalToolkit | Desktop Demo");
        GUI.Label(new Rect(28, 40, 580, 22), "WASD move | Hold right mouse to look | Q/E up/down | Shift faster");
        GUI.Label(new Rect(28, 64, 580, 22), "Space launch physics ball | R reset | Esc quit | Teleports: " + playerTeleports);
    }

    [Serializable]
    private class SmokeResult
    {
        public bool passed;
        public string scene;
        public string graphicsDevice;
        public string graphicsApi;
        public bool linkedPortalPair;
        public bool portalShowsDestination;
        public int playerTeleportCount;
        public int physicsBallTeleportCount;
        public bool playerReachedExit;
        public bool physicsBallReachedExit;
        public int frameCount;
        public string[] errors;
    }
}

