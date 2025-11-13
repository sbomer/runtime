// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using DependencyGraphCore;
using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

namespace DependencyGraphCli.ImGuiUi;

internal sealed class DependencyGraphImGuiApp : IDisposable
{
    private readonly GraphCollection _graphs;
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _inputContext;
    private ImGuiController? _controller;
    private GraphBrowser? _browser;
    private Glfw? _glfw;
    private nint _glfwWindowHandle;
    private bool _disposed;

    private DependencyGraphImGuiApp(GraphCollection graphs)
    {
        _graphs = graphs;
    }

    public static void Run(GraphCollection graphs)
    {
        using var app = new DependencyGraphImGuiApp(graphs);
        app.Start();
    }

    public static bool TryRun(GraphCollection graphs, out string? failureReason)
    {
        if (!IsWindowingSupported(out failureReason))
        {
            return false;
        }

        try
        {
            Run(graphs);
            failureReason = null;
            return true;
        }
        catch (GlfwException ex)
        {
            failureReason = ex.Message;
        }
        catch (DllNotFoundException ex)
        {
            failureReason = ex.Message;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
        }

        return false;
    }

    private void Start()
    {
        var options = WindowOptions.Default;
        options.Title = "Dependency Graph (ImGui)";
        options.Size = new Vector2D<int>(1280, 720);
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        options.FramesPerSecond = 60;
        options.UpdatesPerSecond = 60;
        options.IsEventDriven = false;

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;

        _window.Run();
        CleanupResources();
    }

    private void OnLoad()
    {
        if (_window == null)
        {
            return;
        }

        _inputContext = _window.CreateInput();
        _gl = GL.GetApi(_window);
        InitializeClipboardBackend();
        _controller = new ImGuiController(_gl, _inputContext, GetSystemClipboardText, SetSystemClipboardText, GetKeySymbol);
        _browser = new GraphBrowser(_graphs);
    }

    private void OnRender(double deltaSeconds)
    {
        if (_window == null || _gl == null || _controller == null || _browser == null)
        {
            return;
        }

        _controller.BeginFrame(deltaSeconds, _window.Size, _window.FramebufferSize);

        _gl.ClearColor(0.11f, 0.11f, 0.11f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _browser.Render();
        _controller.Render();
    }

    private void OnClosing()
    {
        CleanupResources();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CleanupResources();
        _window?.Dispose();
        _window = null;
    }

    private void CleanupResources()
    {
        _browser?.Dispose();
        _browser = null;

        _controller?.Dispose();
        _controller = null;

        _inputContext?.Dispose();
        _inputContext = null;

        _gl?.Dispose();
        _gl = null;

        _glfwWindowHandle = nint.Zero;
        _glfw = null;
    }

    private string? GetSystemClipboardText()
    {
        if (_glfw is null || _glfwWindowHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            unsafe
            {
                return _glfw.GetClipboardString((WindowHandle*)_glfwWindowHandle);
            }
        }
        catch
        {
            return null;
        }
    }

    private bool SetSystemClipboardText(string text)
    {
        if (_glfw is null || _glfwWindowHandle == nint.Zero)
        {
            return false;
        }

        try
        {
            unsafe
            {
                _glfw.SetClipboardString((WindowHandle*)_glfwWindowHandle, text);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private string? GetKeySymbol(Key key, int scanCode)
    {
        if (_glfw is null)
        {
            return null;
        }

        try
        {
            string? name = _glfw.GetKeyName((int)key, scanCode);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            return _glfw.GetKeyName((int)Keys.Unknown, scanCode);
        }
        catch
        {
            return null;
        }
    }

    private unsafe void InitializeClipboardBackend()
    {
        _glfwWindowHandle = nint.Zero;
        _glfw = null;

        if (_window is null)
        {
            return;
        }

        if (!GlfwWindowing.IsViewGlfw(_window))
        {
            return;
        }

        Glfw? glfw = GlfwWindowing.GetExistingApi(_window);
        if (glfw is null)
        {
            return;
        }

        WindowHandle* handle = GlfwWindowing.GetHandle(_window);
        if (handle == null)
        {
            return;
        }

        _glfw = glfw;
        _glfwWindowHandle = (nint)handle;
    }

    private static bool IsWindowingSupported(out string? failureReason)
    {
        failureReason = null;

        if (OperatingSystem.IsLinux())
        {
            bool hasX11 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
            bool hasWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            bool hasMir = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MIR_SOCKET"));

            if (!(hasX11 || hasWayland || hasMir))
            {
                failureReason = "A desktop session was not detected (DISPLAY/WAYLAND_DISPLAY/MIR_SOCKET are unset).";
                return false;
            }
        }

        return true;
    }
}
