// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace DependencyGraphCli.ImGuiUi;

internal sealed class ImGuiController : IDisposable
{
    private const int StandardMouseButtonCount = 5;

    private readonly GL _gl;
    private readonly IInputContext _inputContext;
    private readonly List<IKeyboard> _keyboards = new();
    private readonly List<IMouse> _mice = new();

    private readonly bool[] _mouseDown = new bool[StandardMouseButtonCount];
    private readonly bool[] _mouseDownPrevious = new bool[StandardMouseButtonCount];

    private Vector2 _mousePosition = new(float.NaN, float.NaN);
    private Vector2 _mousePositionPrevious = new(float.NaN, float.NaN);
    private Vector2 _scrollDelta = Vector2.Zero;

    private bool _modCtrl;
    private bool _modShift;
    private bool _modAlt;
    private bool _modSuper;
    private bool _lastModCtrl;
    private bool _lastModShift;
    private bool _lastModAlt;
    private bool _lastModSuper;

    private Vector2 _displaySize;
    private Vector2 _framebufferSize;

    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private uint _shader;
    private uint _fontTexture;

    private int _uniformTexture;
    private int _uniformProjection;

    private nint _backendPlatformNamePtr;
    private nint _backendRendererNamePtr;
    private readonly Func<string?> _getSystemClipboard;
    private readonly Func<string, bool> _setSystemClipboard;
    private readonly Func<Key, int, string?>? _getKeySymbol;
    private GCHandle _clipboardHandle;
    private bool _clipboardHandleAllocated;
    private nint _clipboardReturnBuffer;
    private string _lastSyncedClipboard = string.Empty;

    private bool _frameBegun;
    private bool _disposed;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void SetClipboardTextDelegate(void* userData, byte* text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate byte* GetClipboardTextDelegate(void* userData);

    private static readonly unsafe SetClipboardTextDelegate s_setClipboardDelegate = SetClipboardText;
    private static readonly unsafe GetClipboardTextDelegate s_getClipboardDelegate = GetClipboardText;
    private static readonly IntPtr s_setClipboardDelegatePtr = Marshal.GetFunctionPointerForDelegate(s_setClipboardDelegate);
    private static readonly IntPtr s_getClipboardDelegatePtr = Marshal.GetFunctionPointerForDelegate(s_getClipboardDelegate);

    public ImGuiController(GL gl, IInputContext inputContext, Func<string?> getSystemClipboard, Func<string, bool> setSystemClipboard, Func<Key, int, string?>? getKeySymbol)
    {
        _gl = gl;
        _inputContext = inputContext;
        _getSystemClipboard = getSystemClipboard ?? throw new ArgumentNullException(nameof(getSystemClipboard));
        _setSystemClipboard = setSystemClipboard ?? throw new ArgumentNullException(nameof(setSystemClipboard));
        _getKeySymbol = getKeySymbol;

        ImGui.CreateContext();
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        unsafe
        {
            _backendPlatformNamePtr = SilkMarshal.StringToPtr("dependencygraph-cli", NativeStringEncoding.UTF8);
            _backendRendererNamePtr = SilkMarshal.StringToPtr("dependencygraph-cli", NativeStringEncoding.UTF8);
            io.NativePtr->BackendPlatformName = (byte*)_backendPlatformNamePtr;
            io.NativePtr->BackendRendererName = (byte*)_backendRendererNamePtr;
        }
        io.Fonts.AddFontDefault();
        ImGui.StyleColorsDark();

        InstallClipboardIntegration(io);

        HookInputDevices();
        CreateDeviceResources();
    }

    public void BeginFrame(double deltaSeconds, Vector2D<int> windowSize, Vector2D<int> framebufferSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _displaySize = new Vector2(Math.Max(1, windowSize.X), Math.Max(1, windowSize.Y));
        _framebufferSize = new Vector2(Math.Max(1, framebufferSize.X), Math.Max(1, framebufferSize.Y));

        UpdateModifiers();
        UpdateMouseState();

        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = _displaySize;
        io.DisplayFramebufferScale = new Vector2(
            _framebufferSize.X / _displaySize.X,
            _framebufferSize.Y / _displaySize.Y);
        io.DeltaTime = deltaSeconds > 0 ? (float)deltaSeconds : 1f / 60f;

        if (!float.IsNaN(_mousePosition.X) && !float.IsNaN(_mousePosition.Y))
        {
            if (_mousePosition != _mousePositionPrevious)
            {
                io.AddMousePosEvent(_mousePosition.X, _mousePosition.Y);
                _mousePositionPrevious = _mousePosition;
            }
        }
        else if (!_frameBegun)
        {
            io.AddMousePosEvent(float.NaN, float.NaN);
            _mousePositionPrevious = new Vector2(float.NaN, float.NaN);
        }

        for (int i = 0; i < _mouseDown.Length; i++)
        {
            if (_mouseDown[i] != _mouseDownPrevious[i])
            {
                io.AddMouseButtonEvent(i, _mouseDown[i]);
                _mouseDownPrevious[i] = _mouseDown[i];
            }
        }

        EmitModifierEvent(ImGuiKey.ModCtrl, _modCtrl, ref _lastModCtrl);
        EmitModifierEvent(ImGuiKey.ModShift, _modShift, ref _lastModShift);
        EmitModifierEvent(ImGuiKey.ModAlt, _modAlt, ref _lastModAlt);
        EmitModifierEvent(ImGuiKey.ModSuper, _modSuper, ref _lastModSuper);

        if (_scrollDelta != Vector2.Zero)
        {
            io.AddMouseWheelEvent(_scrollDelta.X, _scrollDelta.Y);
            _scrollDelta = Vector2.Zero;
        }

        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_frameBegun)
        {
            return;
        }

        ImGui.Render();
        SyncSystemClipboardFromImGui();
        RenderImDrawData(ImGui.GetDrawData());
        _frameBegun = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        UnhookInputDevices();

        ImGuiIOPtr io = ImGui.GetIO();
        ReleaseClipboardIntegration(io);

        if (_vertexBuffer != 0)
        {
            _gl.DeleteBuffer(_vertexBuffer);
            _vertexBuffer = 0;
        }

        if (_indexBuffer != 0)
        {
            _gl.DeleteBuffer(_indexBuffer);
            _indexBuffer = 0;
        }

        if (_vertexArray != 0)
        {
            _gl.DeleteVertexArray(_vertexArray);
            _vertexArray = 0;
        }

        if (_fontTexture != 0)
        {
            _gl.DeleteTexture(_fontTexture);
            _fontTexture = 0;
        }

        if (_shader != 0)
        {
            _gl.DeleteProgram(_shader);
            _shader = 0;
        }

        if (_backendPlatformNamePtr != nint.Zero || _backendRendererNamePtr != nint.Zero)
        {
            unsafe
            {
                if (_backendPlatformNamePtr != nint.Zero)
                {
                    io.NativePtr->BackendPlatformName = null;
                    SilkMarshal.Free(_backendPlatformNamePtr);
                    _backendPlatformNamePtr = nint.Zero;
                }

                if (_backendRendererNamePtr != nint.Zero)
                {
                    io.NativePtr->BackendRendererName = null;
                    SilkMarshal.Free(_backendRendererNamePtr);
                    _backendRendererNamePtr = nint.Zero;
                }
            }
        }

        ImGui.DestroyContext();
    }

    private void HookInputDevices()
    {
        foreach (var keyboard in _inputContext.Keyboards)
        {
            _keyboards.Add(keyboard);
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        foreach (var mouse in _inputContext.Mice)
        {
            _mice.Add(mouse);
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }
    }

    private void UnhookInputDevices()
    {
        foreach (var keyboard in _keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
            keyboard.KeyChar -= OnKeyChar;
        }
        _keyboards.Clear();

        foreach (var mouse in _mice)
        {
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.MouseMove -= OnMouseMove;
            mouse.Scroll -= OnMouseScroll;
        }
        _mice.Clear();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        ImGuiKey mapped = ImGuiKeyMappings.ToImGuiKey(key);
        if (mapped != ImGuiKey.None)
        {
            io.AddKeyEvent(mapped, true);
        }

        char? actualSymbol = GetActualKeySymbol(key, scanCode);
        if (actualSymbol is char symbolChar && TryGetImGuiKeyForChar(symbolChar, out ImGuiKey symbolKey) && symbolKey != mapped)
        {
            io.AddKeyEvent(symbolKey, true);
        }

        bool ctrlPressed = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        bool superPressed = keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight);
        bool shortcutPressed = ctrlPressed || superPressed;

        if (shortcutPressed)
        {
            char? shortcutKey = GetShortcutKeySymbol(key, actualSymbol);
            if (shortcutKey is char normalized)
            {
                switch (normalized)
                {
                    case 'V':
                        SyncImGuiClipboardFromSystem(injectIntoInput: true);
                        break;
                    case 'C':
                    case 'X':
                        SyncSystemClipboardFromImGui();
                        break;
                }
            }
        }

        UpdateModifiers();
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        ImGuiKey mapped = ImGuiKeyMappings.ToImGuiKey(key);
        if (mapped != ImGuiKey.None)
        {
            io.AddKeyEvent(mapped, false);
        }

        char? actualSymbol = GetActualKeySymbol(key, scanCode);
        if (actualSymbol is char symbolChar && TryGetImGuiKeyForChar(symbolChar, out ImGuiKey symbolKey) && symbolKey != mapped)
        {
            io.AddKeyEvent(symbolKey, false);
        }

        UpdateModifiers();
    }

    private static char? GetShortcutKeySymbol(Key key, char? actualSymbol)
    {
        if (actualSymbol is char existing)
        {
            return existing;
        }

        return key switch
        {
            Key.V => 'V',
            Key.C => 'C',
            Key.X => 'X',
            _ => null,
        };
    }

    private char? GetActualKeySymbol(Key key, int scanCode)
    {
        if (_getKeySymbol is null)
        {
            return null;
        }

        try
        {
            string? symbol = _getKeySymbol(key, scanCode);
            if (string.IsNullOrEmpty(symbol) || symbol.Length != 1)
            {
                return null;
            }

            char candidate = symbol[0];
            if (char.IsLetter(candidate) || char.IsDigit(candidate))
            {
                return char.ToUpperInvariant(candidate);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetImGuiKeyForChar(char c, out ImGuiKey key)
    {
        if (c is >= 'A' and <= 'Z')
        {
            key = (ImGuiKey)((int)ImGuiKey.A + (c - 'A'));
            return true;
        }

        if (c is >= '0' and <= '9')
        {
            key = (ImGuiKey)((int)ImGuiKey._0 + (c - '0'));
            return true;
        }

        key = ImGuiKey.None;
        return false;
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        if (character == 0)
        {
            return;
        }

        ImGui.GetIO().AddInputCharacter(character);
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (TryGetMouseIndex(button, out int index))
        {
            _mouseDown[index] = true;
        }
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (TryGetMouseIndex(button, out int index))
        {
            _mouseDown[index] = false;
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        _mousePosition = position;
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        _scrollDelta += new Vector2(scroll.X, scroll.Y);
    }

    private void UpdateMouseState()
    {
        if (_mice.Count == 0)
        {
            _mousePosition = new Vector2(float.NaN, float.NaN);
            return;
        }

        // Use the first mouse by convention.
        IMouse mouse = _mice[0];
        _mousePosition = mouse.Position;

        _mouseDown[0] = mouse.IsButtonPressed(MouseButton.Left);
        _mouseDown[1] = mouse.IsButtonPressed(MouseButton.Right);
        _mouseDown[2] = mouse.IsButtonPressed(MouseButton.Middle);
        _mouseDown[3] = mouse.IsButtonPressed(MouseButton.Button4);
        _mouseDown[4] = mouse.IsButtonPressed(MouseButton.Button5);
    }

    private void UpdateModifiers()
    {
        bool ctrl = false;
        bool shift = false;
        bool alt = false;
        bool super = false;

        foreach (var keyboard in _keyboards)
        {
            ctrl |= keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
            shift |= keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
            alt |= keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight) || keyboard.IsKeyPressed(Key.Menu);
            super |= keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight);
        }

        _modCtrl = ctrl;
        _modShift = shift;
        _modAlt = alt;
        _modSuper = super;
    }

    private static bool TryGetMouseIndex(MouseButton button, out int index)
    {
        index = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            MouseButton.Button4 => 3,
            MouseButton.Button5 => 4,
            _ => -1,
        };

        return index >= 0;
    }

    private static void EmitModifierEvent(ImGuiKey key, bool current, ref bool previous)
    {
        if (current != previous)
        {
            ImGui.GetIO().AddKeyEvent(key, current);
            previous = current;
        }
    }

    private void CreateDeviceResources()
    {
        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();
        _indexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);

        _shader = CreateShaderProgram();
        _uniformTexture = _gl.GetUniformLocation(_shader, "Texture");
        _uniformProjection = _gl.GetUniformLocation(_shader, "Projection");

        const int positionLocation = 0;
        const int uvLocation = 1;
        const int colorLocation = 2;

        int stride = Unsafe.SizeOf<ImDrawVert>();

        unsafe
        {
            _gl.EnableVertexAttribArray((uint)positionLocation);
            _gl.VertexAttribPointer((uint)positionLocation, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);

            _gl.EnableVertexAttribArray((uint)uvLocation);
            _gl.VertexAttribPointer((uint)uvLocation, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)8);

            _gl.EnableVertexAttribArray((uint)colorLocation);
            _gl.VertexAttribPointer((uint)colorLocation, 4, VertexAttribPointerType.UnsignedByte, true, (uint)stride, (void*)16);
        }

        RecreateFontTexture();

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
    }

    private uint CreateShaderProgram()
    {
        uint vertex = _gl.CreateShader(ShaderType.VertexShader);
        uint fragment = _gl.CreateShader(ShaderType.FragmentShader);

        const string vertexSource = "#version 330 core\n" +
            "layout (location = 0) in vec2 in_position;\n" +
            "layout (location = 1) in vec2 in_texCoord;\n" +
            "layout (location = 2) in vec4 in_color;\n" +
            "uniform mat4 Projection;\n" +
            "out vec2 frag_uv;\n" +
            "out vec4 frag_color;\n" +
            "void main()\n" +
            "{\n" +
            "    frag_uv = in_texCoord;\n" +
            "    frag_color = in_color;\n" +
            "    gl_Position = Projection * vec4(in_position, 0.0, 1.0);\n" +
            "}\n";

        const string fragmentSource = "#version 330 core\n" +
            "in vec2 frag_uv;\n" +
            "in vec4 frag_color;\n" +
            "uniform sampler2D Texture;\n" +
            "out vec4 out_color;\n" +
            "void main()\n" +
            "{\n" +
            "    out_color = frag_color * texture(Texture, frag_uv);\n" +
            "}\n";

        _gl.ShaderSource(vertex, vertexSource);
        _gl.CompileShader(vertex);
        CheckShader(vertex);

        _gl.ShaderSource(fragment, fragmentSource);
        _gl.CompileShader(fragment);
        CheckShader(fragment);

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);
        CheckProgram(program);

        _gl.DetachShader(program, vertex);
        _gl.DetachShader(program, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);

        return program;
    }

    private void RecreateFontTexture()
    {
        unsafe
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out _);

            _fontTexture = _gl.GenTexture();
            _gl.BindTexture(GLEnum.Texture2D, _fontTexture);
            _gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
            _gl.PixelStore(GLEnum.UnpackAlignment, 1);
            _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            io.Fonts.SetTexID((nint)_fontTexture);
            _gl.BindTexture(GLEnum.Texture2D, 0);
        }
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.NativePtr == null || drawData.CmdListsCount == 0)
        {
            return;
        }

        int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);

        if (fbWidth <= 0 || fbHeight <= 0)
        {
            return;
        }

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        _gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);
        _gl.Enable(GLEnum.Blend);
        _gl.BlendEquation(GLEnum.FuncAdd);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        _gl.Disable(GLEnum.CullFace);
        _gl.Disable(GLEnum.DepthTest);
        _gl.Enable(GLEnum.ScissorTest);
        _gl.ActiveTexture(TextureUnit.Texture0);

        _gl.UseProgram(_shader);
        _gl.Uniform1(_uniformTexture, 0);

        Matrix4x4 projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(
            drawData.DisplayPos.X,
            drawData.DisplayPos.X + drawData.DisplaySize.X,
            drawData.DisplayPos.Y + drawData.DisplaySize.Y,
            drawData.DisplayPos.Y,
            -1.0f,
            1.0f);

        unsafe
        {
            _gl.UniformMatrix4(_uniformProjection, 1, false, (float*)&projectionMatrix);
        }

        _gl.BindVertexArray(_vertexArray);

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            nuint vertexBufferSize = (nuint)(cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>());
            nuint indexBufferSize = (nuint)(cmdList.IdxBuffer.Size * sizeof(ushort));

            unsafe
            {
                _gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
                _gl.BufferData(GLEnum.ArrayBuffer, vertexBufferSize, (void*)cmdList.VtxBuffer.Data, GLEnum.StreamDraw);
                _gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
                _gl.BufferData(GLEnum.ElementArrayBuffer, indexBufferSize, (void*)cmdList.IdxBuffer.Data, GLEnum.StreamDraw);
            }

            int indexOffset = 0;

            for (int cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++)
            {
                ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdIndex];

                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    // User callbacks are not expected in this tool; skip safely.
                    indexOffset += (int)pcmd.ElemCount;
                    continue;
                }

                uint textureId = pcmd.TextureId == nint.Zero ? _fontTexture : (uint)pcmd.TextureId;
                _gl.BindTexture(GLEnum.Texture2D, textureId);

                Vector4 clip = pcmd.ClipRect;
                int clipX = (int)clip.X;
                int clipY = (int)(fbHeight - clip.W);
                int clipWidth = (int)(clip.Z - clip.X);
                int clipHeight = (int)(clip.W - clip.Y);

                if (clipWidth <= 0 || clipHeight <= 0)
                {
                    indexOffset += (int)pcmd.ElemCount;
                    continue;
                }

                _gl.Scissor(clipX, clipY, (uint)clipWidth, (uint)clipHeight);

                unsafe
                {
                    _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, (uint)pcmd.ElemCount, DrawElementsType.UnsignedShort, (void*)(indexOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
                }

                indexOffset += (int)pcmd.ElemCount;
            }
        }

        _gl.Disable(GLEnum.ScissorTest);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    private void CheckShader(uint shader)
    {
        const GLEnum status = GLEnum.CompileStatus;
        if (_gl.GetShader(shader, status) == (int)GLEnum.True)
        {
            return;
        }

        string log = _gl.GetShaderInfoLog(shader);
        throw new InvalidOperationException($"Failed to compile ImGui shader: {log}");
    }

    private void CheckProgram(uint program)
    {
        const GLEnum status = GLEnum.LinkStatus;
        if (_gl.GetProgram(program, status) == (int)GLEnum.True)
        {
            return;
        }

        string log = _gl.GetProgramInfoLog(program);
        throw new InvalidOperationException($"Failed to link ImGui shader program: {log}");
    }

    private void SyncImGuiClipboardFromSystem(bool injectIntoInput)
    {
        string? systemClipboard = TryGetSystemClipboardText();
        if (string.IsNullOrEmpty(systemClipboard))
        {
            return;
        }

        if (!string.Equals(systemClipboard, _lastSyncedClipboard, StringComparison.Ordinal))
        {
            ImGui.SetClipboardText(systemClipboard);
            _lastSyncedClipboard = systemClipboard;
        }

        if (injectIntoInput)
        {
            ImGui.GetIO().AddInputCharactersUTF8(systemClipboard);
        }
    }

    private void SyncSystemClipboardFromImGui()
    {
        string clipboard = GetImGuiClipboardTextSafe();
        if (string.Equals(clipboard, _lastSyncedClipboard, StringComparison.Ordinal))
        {
            return;
        }

        if (TrySetSystemClipboardText(clipboard))
        {
            _lastSyncedClipboard = clipboard;
        }
    }

    private static string GetImGuiClipboardTextSafe()
    {
        try
        {
            return ImGui.GetClipboardText() ?? string.Empty;
        }
        catch (NullReferenceException)
        {
            return string.Empty;
        }
    }

    private string? TryGetSystemClipboardText()
    {
        try
        {
            return _getSystemClipboard();
        }
        catch
        {
            return null;
        }
    }

    private bool TrySetSystemClipboardText(string text)
    {
        try
        {
            return _setSystemClipboard(text);
        }
        catch
        {
            return false;
        }
    }

    private unsafe void InstallClipboardIntegration(ImGuiIOPtr io)
    {
        io.SetClipboardTextFn = s_setClipboardDelegatePtr;
        io.GetClipboardTextFn = s_getClipboardDelegatePtr;

        if (_clipboardHandleAllocated)
        {
            _clipboardHandle.Free();
            _clipboardHandleAllocated = false;
        }

        _clipboardHandle = GCHandle.Alloc(this);
        _clipboardHandleAllocated = true;
        io.ClipboardUserData = GCHandle.ToIntPtr(_clipboardHandle);
    }

    private void ReleaseClipboardIntegration(ImGuiIOPtr io)
    {
        io.SetClipboardTextFn = IntPtr.Zero;
        io.GetClipboardTextFn = IntPtr.Zero;
        io.ClipboardUserData = IntPtr.Zero;

        if (_clipboardHandleAllocated)
        {
            _clipboardHandle.Free();
            _clipboardHandleAllocated = false;
        }

        ReleaseClipboardReturnBuffer();
    }

    private void ReleaseClipboardReturnBuffer()
    {
        if (_clipboardReturnBuffer != nint.Zero)
        {
            SilkMarshal.Free(_clipboardReturnBuffer);
            _clipboardReturnBuffer = nint.Zero;
        }
    }

    private unsafe void HandleSetClipboard(byte* text)
    {
        string clipboardText = text == null ? string.Empty : SilkMarshal.PtrToString((nint)text, NativeStringEncoding.UTF8) ?? string.Empty;
        if (TrySetSystemClipboardText(clipboardText))
        {
            _lastSyncedClipboard = clipboardText;
        }
    }

    private unsafe byte* HandleGetClipboard()
    {
        string clipboardText = TryGetSystemClipboardText() ?? string.Empty;
        if (!string.Equals(clipboardText, _lastSyncedClipboard, StringComparison.Ordinal))
        {
            _lastSyncedClipboard = clipboardText;
        }

        ReleaseClipboardReturnBuffer();
        _clipboardReturnBuffer = SilkMarshal.StringToPtr(clipboardText, NativeStringEncoding.UTF8);
        return (byte*)_clipboardReturnBuffer;
    }

    private static unsafe void SetClipboardText(void* userData, byte* text)
    {
        if (userData == null)
        {
            return;
        }

        GCHandle handle = GCHandle.FromIntPtr((nint)userData);
        if (!handle.IsAllocated || handle.Target is not ImGuiController controller)
        {
            return;
        }

        controller.HandleSetClipboard(text);
    }

    private static unsafe byte* GetClipboardText(void* userData)
    {
        if (userData == null)
        {
            return null;
        }

        GCHandle handle = GCHandle.FromIntPtr((nint)userData);
        if (!handle.IsAllocated || handle.Target is not ImGuiController controller)
        {
            return null;
        }

        return controller.HandleGetClipboard();
    }
}
