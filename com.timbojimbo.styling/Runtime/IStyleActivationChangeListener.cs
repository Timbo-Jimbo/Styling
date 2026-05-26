using System.Collections.Generic;

namespace TimboJimbo.Styling
{
    public interface IStyleActivationChangeListener
    {
        void OnStyleActivationsChanged(List<string> activeStyles);
    }
}