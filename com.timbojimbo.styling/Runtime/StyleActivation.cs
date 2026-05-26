using System;

namespace TimboJimbo.Styling
{
    [Serializable]
    public struct StyleActivation
    {
        public string Name;
        public bool Active;

        public StyleActivation(string name, bool active = true)
        {
            Name = name;
            Active = active;
        }
    }
}