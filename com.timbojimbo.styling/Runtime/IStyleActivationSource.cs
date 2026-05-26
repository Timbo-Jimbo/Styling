using System.Collections.Generic;

namespace TimboJimbo.Styling
{
    /// <summary>
    /// Interface for components that provide style activations to the Styling system.
    /// </summary>
    public interface IStyleActivationSource
    {
        void GetStyleActivations(List<StyleActivation> activations);
    }
}