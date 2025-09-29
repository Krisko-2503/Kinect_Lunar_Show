using System.Windows;
using System.Windows.Media;

namespace KinectLunnar
{
    public class VisualHost : FrameworkElement
    {
        private readonly VisualCollection _children;
        public DrawingVisual Visual { get; }

        public VisualHost()
        {
            _children = new VisualCollection(this);
            Visual = new DrawingVisual();
            _children.Add(Visual);
        }

        protected override int VisualChildrenCount => _children.Count;
        protected override Visual GetVisualChild(int index) => _children[index];
    }
}
