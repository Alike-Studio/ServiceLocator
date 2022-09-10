﻿using System;

namespace BrunoMikoski.ServicesLocation
{
    public abstract class ServiceDependentBase : IDependsOnService
    {
        public abstract void OnServicesDependenciesResolved();
        
        protected ServiceDependentBase()
        {
            ServiceLocator.Instance.ResolveDependencies(this);
        }
    }
}
