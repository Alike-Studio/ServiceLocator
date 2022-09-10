using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITASK_ENABLED
using Cysharp.Threading.Tasks;
#endif


namespace BrunoMikoski.ServicesLocation
{
    public class ServiceLocator 
    {
        private static ServiceLocator instance;
        public static ServiceLocator Instance
        {
            get
            {
                if (instance == null)
                    instance = new ServiceLocator();
                return instance;
            }
        }

        private static Dictionary<Type, object> typeToInstances = new Dictionary<Type, object>();

        private static Dictionary<Type, List<IServiceObservable>> typeToObservables =
            new Dictionary<Type, List<IServiceObservable>>();

        private List<IDependsOnExplicitServices> waitingOnDependenciesTobeResolved = new List<IDependsOnExplicitServices>();

        private Dictionary<IDependsOnExplicitServices, Type> waitingDependenciesBeResolvedToRegister =
            new Dictionary<IDependsOnExplicitServices, Type>();

        private static DependencyCache dependencies = new DependencyCache();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void LoadAOTDependencies()
        {
            TextAsset aotFile = Resources.Load<TextAsset>("ServiceLocatorAOTDependencies");
            if (aotFile == null)
                return;

            JsonUtility.FromJsonOverwrite(aotFile.text, dependencies);
            dependencies.Parse();
        }
        
        public void RegisterInstance<T>(T instance)
        {
            Type type = typeof(T);
            RegisterInstance(type, instance);
        }

        private void RegisterInstance(Type type, object instance)
        {
            if (!CanRegisterService(type, instance))
                return;

            if (instance is IDependsOnExplicitServices serviceDependent)
            {
                if (!IsDependenciesResolved(serviceDependent))
                {
                    if (!waitingOnDependenciesTobeResolved.Contains(serviceDependent))
                        waitingOnDependenciesTobeResolved.Add(serviceDependent);

                    waitingDependenciesBeResolvedToRegister.Add(serviceDependent, type);
                    return;
                }
            }
            
            typeToInstances.Add(type, instance);
            TryResolveDependencies();
            DispatchOnRegistered(type, instance);
        }

        private void DispatchOnRegistered(Type type, object instance)
        {
            if (instance is IOnServiceRegistered onRegistered)
            {
                onRegistered.OnRegisteredOnServiceLocator(this);
            }

            if (instance is IDependsOnExplicitServices serviceDependent)
            {
                serviceDependent.OnServicesDependenciesResolved();
            }

            if (typeToObservables.TryGetValue(type, out List<IServiceObservable> observables))
            {
                for (int i = 0; i < observables.Count; i++)
                    observables[i].OnServiceRegistered(type);
            }
        }

        private bool CanRegisterService(Type type, object instance)
        {
            if (HasService(type))
            {
                Debug.LogError($"Service of type {type} is already registered.");
                return false;
            }

            if (instance is IConditionalService conditionalService)
            {
                if (!conditionalService.CanBeRegistered(this))
                    return false;
            }

            return true;
        }

        public bool HasService<T>()
        {
            return HasService(typeof(T));
        }

        private bool HasService(Type type)
        {
            return typeToInstances.ContainsKey(type);
        }

        public T GetInstance<T>() where T : class
        {
            Type type = typeof(T);
            if (typeToInstances.TryGetValue(type, out object instanceObject))
                return instanceObject as T;

            if (Application.isPlaying)
            {
                Debug.LogError(
                    $"The Service {typeof(T)} is not yet registered on the ServiceLocator, " +
                    $"consider implementing IDependsOnExplicitServices interface");
            }
            else
            {
                if (type.IsSubclassOf(typeof(Object)))
                {
                    Object instanceType = Object.FindObjectOfType(type);
                    if (instanceType != null)
                        return instanceType as T;
#if UNITY_EDITOR
                    string[] guids = UnityEditor.AssetDatabase.FindAssets($"{type.Name} t:Prefab");
                    if (guids.Length > 0)
                    {
                        return UnityEditor.AssetDatabase.LoadAssetAtPath(
                            UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]), type) as T;
                    }
#endif                    
                    Debug.LogError($"Failed to find any Object of type: {typeof(T)}, check if the object you need is available on the scene");
                    return null;
                }

                T instance = (T)Activator.CreateInstance(type);
                return instance;
            }
            
            return null;
        }

        public void UnregisterAllServices()
        {
            List<object> activeInstances = new List<object>(typeToInstances.Count);
            foreach (var typeToInstance in typeToInstances)
                activeInstances.Add(typeToInstance.Value);

            for (int i = activeInstances.Count - 1; i >= 0; i--)
            {
                UnregisterInstance(activeInstances[i]);
            }
            
            typeToInstances.Clear();

            if (waitingOnDependenciesTobeResolved.Count > 0)
            {
                Debug.LogWarning($"{waitingOnDependenciesTobeResolved.Count} dependencies was waiting to be resolved");
                waitingOnDependenciesTobeResolved.Clear();
                waitingDependenciesBeResolvedToRegister.Clear();
                typeToObservables.Clear();
            }
        }
        
        public void UnregisterInstance<T>()
        {
            Type type = typeof(T);
            UnregisterInstance(type);
        }
        
        public void UnregisterInstance<T>(T instance)
        {
            Type type = instance.GetType();
            UnregisterInstance(type);
        }

        public void UnregisterInstance(Type targetType)
        {
            if (!typeToInstances.TryGetValue(targetType, out object serviceInstance)) 
                return;
            
            DispatchOnUnregisteredService(targetType, serviceInstance);
            typeToInstances.Remove(targetType);
        }

        private void DispatchOnUnregisteredService(Type targetType, object serviceInstance)
        {
            if (serviceInstance is IOnServiceUnregistered onServiceUnregistered)
            {
                onServiceUnregistered.OnUnregisteredFromServiceLocator(this);
            }
            
            if (typeToObservables.TryGetValue(targetType, out List<IServiceObservable> observables))
            {
                for (int i = 0; i < observables.Count; i++)
                    observables[i].OnServiceUnregistered(targetType);
            }
        }

        public void SubscribeToServiceChanges<T>(IServiceObservable observable)
        {
            Type type = typeof(T);
            if (!typeToObservables.ContainsKey(type))
                typeToObservables.Add(type, new List<IServiceObservable>());

            if (!typeToObservables[type].Contains(observable))
                typeToObservables[type].Add(observable);
        }
        
        public void UnsubscribeToServiceChanges<T>(IServiceObservable observable)
        {
            Type type = typeof(T);
            if (!typeToObservables.TryGetValue(type, out List<IServiceObservable> observables))
                return;

            observables.Remove(observable);
        }

        private void TryResolveDependencies()
        {
            for (int i = waitingOnDependenciesTobeResolved.Count - 1; i >= 0; i--)
            {
                IDependsOnExplicitServices dependsOnServices = waitingOnDependenciesTobeResolved[i];
                if (!IsDependenciesResolved(dependsOnServices)) 
                    continue;
                
                waitingOnDependenciesTobeResolved.Remove(dependsOnServices);

                if (waitingDependenciesBeResolvedToRegister.ContainsKey(dependsOnServices))
                {
                    RegisterInstance(waitingDependenciesBeResolvedToRegister[dependsOnServices], dependsOnServices);
                    waitingDependenciesBeResolvedToRegister.Remove(dependsOnServices);
                }
                
                dependsOnServices.OnServicesDependenciesResolved();
            }
        }

        private bool IsDependenciesResolved(IDependsOnExplicitServices dependsOnServices)
        {
            for (int i = 0; i < dependsOnServices.DependsOnServices.Length; i++)
            {
                Type dependentType = dependsOnServices.DependsOnServices[i];
                if (!HasService(dependentType))
                    return false;
            }

            return true;
        }

#if UNITASK_ENABLED
        public async UniTask WaitForServiceAsync<T>() where T : class
        {
            await UniTask.WaitUntil(HasService<T>);
        }
#endif

        public void ResolveDependencies(IDependsOnExplicitServices serviceDependent)
        {
            waitingOnDependenciesTobeResolved.Add(serviceDependent);
            
            
            TryResolveDependencies();
        }

        public void ResolveDependencies(object targetObject)
        {
            List<Type> dependencies = new List<Type>();
            MemberInfo[] memberInfos = targetObject.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public |
                                                                         BindingFlags.Static | BindingFlags.NonPublic);
            Type serviceReferenceType = typeof(ServiceReference<>);
            
            for (int i = 0; i < memberInfos.Length; i++)
            {
                MemberInfo info = memberInfos[i];
                if (info.MemberType != MemberTypes.Field)
                    continue;

                FieldInfo fieldInfo = ((FieldInfo) info);

                if (!fieldInfo.FieldType.IsGenericType)
                    continue;
                
                if (fieldInfo.FieldType.GetGenericTypeDefinition() == serviceReferenceType)
                {
                    Type type = fieldInfo.FieldType.GetGenericArguments()[0];
                    if(dependencies.Contains(type))
                        continue;

                    dependencies.Add(type);
                }
            }

            if (dependencies.Count > 0)
                Debug.Log($"{targetObject} depends on {string.Join(',', dependencies)} ");
        }
    }
}
