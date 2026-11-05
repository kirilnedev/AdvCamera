using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AdvCamera.Camera;

/// <summary>UI wrapper around a <see cref="HardwareControl"/>; changes are sent to the camera immediately.</summary>
public sealed class HardwareControlViewModel : INotifyPropertyChanged
{
    private readonly HardwareControl _control;
    private double _value;
    private bool _isAuto;
    private bool _loading;

    public HardwareControlViewModel(HardwareControl control)
    {
        _control = control;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Name => _control.Name;
    public double Min => _control.Min;
    public double Max => _control.Max;
    public double Step => _control.Step;
    public double LargeStep => Math.Max(_control.Step, Math.Round((Max - Min) / 10 / _control.Step) * _control.Step);
    public Visibility AutoVisibility => _control.CanAuto ? Visibility.Visible : Visibility.Collapsed;
    public bool IsManual => !_isAuto;

    public double Value
    {
        get => _value;
        set
        {
            value = Math.Round(value);
            if (value == _value) return;
            _value = value;
            Notify();
            if (!_loading) _control.TrySet((int)value, _isAuto);
        }
    }

    public bool IsAuto
    {
        get => _isAuto;
        set
        {
            if (value == _isAuto) return;
            _isAuto = value;
            Notify();
            Notify(nameof(IsManual));
            if (_loading) return;
            _control.TrySet((int)_value, value);
            if (!value) Refresh(); // pick up the value the auto mode settled on
        }
    }

    public void Refresh()
    {
        if (!_control.TryGet(out int v, out bool auto)) return;
        _loading = true;
        try
        {
            Value = v;
            IsAuto = auto && _control.CanAuto;
        }
        finally
        {
            _loading = false;
        }
    }

    public void RestoreDefault()
    {
        _control.TrySet(_control.Default, _control.CanAuto);
        Refresh();
    }
}
