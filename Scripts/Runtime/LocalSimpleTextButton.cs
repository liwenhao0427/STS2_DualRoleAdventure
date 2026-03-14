using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace LocalMultiControl.Scripts.Runtime;

internal sealed partial class LocalSimpleTextButton : NButton
{
    private const string ButtonTexturePath = "res://images/packed/common_ui/event_button.png";
    private const string HsvShaderPath = "res://shaders/hsv.gdshader";

    private TextureRect? _image;
    private Label? _label;
    private ShaderMaterial? _hsv;
    private Tween? _hoverTween;
    private string _buttonText = string.Empty;

    public string ButtonText
    {
        get => _buttonText;
        set
        {
            _buttonText = value;
            if (_label != null)
            {
                _label.Text = _buttonText;
            }
        }
    }

    public override void _Ready()
    {
        BuildVisualTree();
        ConnectSignals();
        ApplyDefaultVisualState();
    }

    protected override void OnFocus()
    {
        base.OnFocus();
        _hoverTween?.Kill();
        UpdateShaderValue(1.15f);
        Scale = Vector2.One * 1.04f;
    }

    protected override void OnUnfocus()
    {
        _hoverTween?.Kill();
        _hoverTween = CreateTween().SetParallel();
        _hoverTween.TweenMethod(Callable.From<float>(UpdateShaderValue), 1.15f, 1f, 0.2);
        _hoverTween.TweenProperty(this, "scale", Vector2.One, 0.2);
    }

    protected override void OnPress()
    {
        base.OnPress();
        _hoverTween?.Kill();
        UpdateShaderValue(0.85f);
        Scale = Vector2.One * 0.98f;
    }

    protected override void OnRelease()
    {
        if (IsFocused)
        {
            UpdateShaderValue(1.15f);
            Scale = Vector2.One * 1.04f;
            return;
        }

        ApplyDefaultVisualState();
    }

    private void BuildVisualTree()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(120f, 52f);
        PivotOffset = CustomMinimumSize * 0.5f;

        _image ??= CreateImageNode();
        _label ??= CreateLabelNode();

        if (_image.GetParent() == null)
        {
            AddChild(_image);
        }

        if (_label.GetParent() == null)
        {
            AddChild(_label);
        }

        _label.Text = _buttonText;
    }

    private TextureRect CreateImageNode()
    {
        TextureRect image = new TextureRect
        {
            Name = "Image",
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore,
            Texture = ResourceLoader.Load<Texture2D>(ButtonTexturePath, null, ResourceLoader.CacheMode.Reuse)
        };

        Shader? shader = ResourceLoader.Load<Shader>(HsvShaderPath, null, ResourceLoader.CacheMode.Reuse);
        if (shader != null)
        {
            _hsv = new ShaderMaterial
            {
                ResourceLocalToScene = true,
                Shader = shader
            };
            _hsv.SetShaderParameter("h", 1f);
            _hsv.SetShaderParameter("s", 1f);
            _hsv.SetShaderParameter("v", 1f);
            image.Material = _hsv;
        }

        return image;
    }

    private Label CreateLabelNode()
    {
        Label label = new Label
        {
            Name = "Label",
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -40f,
            OffsetTop = -16f,
            OffsetRight = 40f,
            OffsetBottom = 16f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", new Color("fdf6e3"));
        label.AddThemeColorOverride("font_outline_color", new Color("13202e"));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25f));
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeFontSizeOverride("font_size", 22);
        return label;
    }

    private void ApplyDefaultVisualState()
    {
        UpdateShaderValue(1f);
        Scale = Vector2.One;
    }

    private void UpdateShaderValue(float value)
    {
        _hsv?.SetShaderParameter("v", value);
    }
}

