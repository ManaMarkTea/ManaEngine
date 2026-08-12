using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// View model behind the ManaUI showcase. Written exactly the way real editor view models should be --
    /// source-generated observable properties and relay commands, no reference to any UI type.
    /// </summary>
    public partial class ManaUiShowcaseViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "ManaUI";

        [ObservableProperty]
        private float _brushRadius = 4.0f;

        [ObservableProperty]
        private bool _showWireframe;

        [ObservableProperty]
        private int _clickCount;

        public ObservableCollection<ShowcaseLayerViewModel> Layers { get; } = new();

        public ManaUiShowcaseViewModel()
        {
            Layers.Add(new ShowcaseLayerViewModel("Terrain", 18420));
            Layers.Add(new ShowcaseLayerViewModel("Props", 2145));
        }

        /// <summary>Derived text; re-raised whenever the value it depends on changes.</summary>
        public string BrushSummary => $"Brush radius {BrushRadius:F1}";

        partial void OnBrushRadiusChanged(float value)
        {
            OnPropertyChanged(nameof(BrushSummary));
        }

        [RelayCommand]
        private void AddLayer()
        {
            Layers.Add(new ShowcaseLayerViewModel($"Layer {Layers.Count + 1}", Random.Shared.Next(100, 9999)));
            RemoveLayerCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanRemoveLayer))]
        private void RemoveLayer()
        {
            if (Layers.Count > 0)
            {
                Layers.RemoveAt(Layers.Count - 1);
            }

            RemoveLayerCommand.NotifyCanExecuteChanged();
        }

        private bool CanRemoveLayer() => Layers.Count > 0;

        [RelayCommand]
        private void Bump()
        {
            ClickCount++;
        }
    }

    public partial class ShowcaseLayerViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private int _voxelCount;

        public ShowcaseLayerViewModel(string name, int voxelCount)
        {
            _name = name;
            _voxelCount = voxelCount;
        }
    }
}
