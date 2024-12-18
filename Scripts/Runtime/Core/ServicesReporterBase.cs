using UnityEngine;

namespace BrunoMikoski.ServicesLocation
{
    [DefaultExecutionOrder(-1000)]
    public abstract class ServicesReporterBase : MonoBehaviour
    {
        protected virtual void Awake()
        {
            RegisterServices();
        }
        
        protected virtual void OnDestroy()
        {
            UnregisterServices();
        }

        public virtual void Initialize() { }
        protected abstract void RegisterServices();
        protected abstract void UnregisterServices();

    }
}
