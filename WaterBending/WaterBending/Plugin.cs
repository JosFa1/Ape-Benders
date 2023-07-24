using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using Utilla;
using GorillaLocomotion;
using Photon.Voice;
using GorillaLocomotion.Swimming;
using GorillaLocomotion.Gameplay;
using System.Runtime;
using HarmonyLib;
using System.IO.Compression;
using System.Collections;
using System.Threading.Tasks;

namespace WaterBending
{
    public class WaterBlobObjectPool
    {
        private GameObject waterBlobPrefab;
        private List<GameObject> waterBlobInstances = new List<GameObject>();

        public WaterBlobObjectPool(GameObject prefab)
        {
            waterBlobPrefab = prefab;
        }

        public GameObject GetWaterBlobInstance(Vector3 position, Quaternion rotation)
        {
            GameObject waterBlobInstance = null;

            for (int i = 0; i < waterBlobInstances.Count; i++)
            {
                if (!waterBlobInstances[i].activeSelf)
                {
                    waterBlobInstance = waterBlobInstances[i];
                    waterBlobInstance.transform.position = position;
                    waterBlobInstance.transform.rotation = rotation;
                    waterBlobInstance.SetActive(true);
                    break;
                }
            }

            if (waterBlobInstance == null)
            {
                waterBlobInstance = UnityEngine.Object.Instantiate(waterBlobPrefab, position, rotation);
                waterBlobInstances.Add(waterBlobInstance);
            }

            return waterBlobInstance;
        }

        public void ReturnWaterBlobInstance(GameObject waterBlobInstance)
        {
            waterBlobInstance.SetActive(false);
        }

        public void Clear()
        {
            foreach (GameObject waterBlobInstance in waterBlobInstances)
            {
                UnityEngine.Object.Destroy(waterBlobInstance);
            }
            waterBlobInstances.Clear();
        }

        public void DeleteAllWater()
        {
            foreach (GameObject waterBlobInstance in waterBlobInstances)
            {
                bool hasRigidbody = waterBlobInstance.GetComponent<Rigidbody>();
                if (!hasRigidbody)
                {
                    waterBlobInstance.AddComponent<Rigidbody>();
                }
            }
            DelayedAction();
        }

        public async Task DelayedAction()
        {
            // Delay for 2 seconds
            await Task.Delay(TimeSpan.FromSeconds(15));

            // After the delay, remove the rigidbodies and destroy the waterBlobInstances
            foreach (GameObject waterBlobInstance in waterBlobInstances)
            {
                Rigidbody rigidbody = waterBlobInstance.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    UnityEngine.Object.Destroy(rigidbody);
                }

                ReturnWaterBlobInstance(waterBlobInstance);
                UnityEngine.Object.Destroy(waterBlobInstance);
            }

            // Clear the list of waterBlobInstances after they are deleted
            waterBlobInstances.Clear();
        }

    }

    [ModdedGamemode]
    [BepInDependency("org.legoandmars.gorillatag.utilla", "1.5.0")]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        bool inRoom;
        private bool rightGrip = false;
        private bool leftGrip = false;
        private bool rightTrigger = false;
        private bool leftTrigger = false;
        private bool secondaryR;
        private bool secondaryL;
        private bool waterPlacing = false;
        private float distanceFromAL = 5f;
        private float scaleFactor;
        private Quaternion righthandR;
        private Quaternion lefthandR;
        private Vector3 righthandP;
        private Vector3 lefthandP;
        private Quaternion averageRotation;
        private Vector3 averageDistance;
        private Vector3 spawnPosition;
        private Vector2 JoystickR;
        private XRNode leftHandNode = XRNode.LeftHand;
        private XRNode rightHandNode = XRNode.RightHand;
        private Vector3 TempRight;
        private Vector3 TempLeft;

        private float scaleFactorSet;
        private Vector3 averageDistanceSet;
        private Quaternion averageRotationSet;

        private WaterBlobObjectPool waterBlobObjectPool;
        private GameObject activeWaterBlobInstance;
        private GameObject Ocean;
        private GameObject NewOcean;
        private List<GameObject> temporaryWaterBlobInstances = new List<GameObject>();

        private bool isPointing = false;
        private bool isActivateFall = false;
        private bool isActivateFallPrev = false;
        private bool isHandFall = false;
        private bool prevIsPointing = false;
        private List<GameObject> waterBlobInstancesList = new List<GameObject>();
        WaterParameters settings;

        void Start()
        {
            Utilla.Events.GameInitialized += OnGameInitialized;
        }

        void OnEnable()
        {
            HarmonyPatches.ApplyHarmonyPatches();
        }

        void OnDisable()
        {
            HarmonyPatches.RemoveHarmonyPatches();
            DeleteAllWater();
        }

        public AssetBundle LoadAssetBundle(string path)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
            AssetBundle bundle = AssetBundle.LoadFromStream(stream);
            stream.Close();
            return bundle;
        }

        void OnGameInitialized(object sender, EventArgs e)
        {
            try
            {
                var bundle = LoadAssetBundle("WaterBending.Resources.apebendingassets");
                foreach (var name in bundle.GetAllAssetNames())
                {
                    Console.WriteLine(name);
                }
                GameObject waterBlobPrefab = bundle.LoadAsset<GameObject>("water");
                waterBlobPrefab.SetActive(false);
                settings = ScriptableObject.CreateInstance<WaterParameters>();
                Traverse.Create(waterBlobPrefab).Field("settings").SetValue(settings);
                WaterVolume waterVolumeComponent = waterBlobPrefab.GetComponent<WaterVolume>();
                waterBlobObjectPool = new WaterBlobObjectPool(waterBlobPrefab);
                Ocean = GameObject.Find("OceanWater");
                NewOcean = Instantiate(Ocean);
                NewOcean.transform.localScale = new Vector3 (2f, 2f, 2f);
                NewOcean.transform.parent = waterBlobPrefab.transform;
                NewOcean.transform.localPosition = new Vector3 (0, 0, 0);
                NewOcean.transform.localRotation = Quaternion.identity;
            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }
        }

        void Update()
        {
            if (inRoom)
            {
                InputDevices.GetDeviceAtXRNode(leftHandNode).TryGetFeatureValue(CommonUsages.secondaryButton, out secondaryL);
                InputDevices.GetDeviceAtXRNode(rightHandNode).TryGetFeatureValue(CommonUsages.secondaryButton, out secondaryR);
                InputDevices.GetDeviceAtXRNode(leftHandNode).TryGetFeatureValue(CommonUsages.gripButton, out leftGrip);
                InputDevices.GetDeviceAtXRNode(rightHandNode).TryGetFeatureValue(CommonUsages.gripButton, out rightGrip);
                InputDevices.GetDeviceAtXRNode(leftHandNode).TryGetFeatureValue(CommonUsages.triggerButton, out leftTrigger);
                InputDevices.GetDeviceAtXRNode(rightHandNode).TryGetFeatureValue(CommonUsages.triggerButton, out rightTrigger);
                prevIsPointing = isPointing;
                isPointing = Pointing();
                isActivateFall = activateFall();
                isHandFall = HandFall();
                if (isPointing)
                {
                    InputDevices.GetDeviceAtXRNode(rightHandNode).TryGetFeatureValue(CommonUsages.primary2DAxis, out JoystickR);
                    if (distanceFromAL >= 1)
                    {
                        distanceFromAL = distanceFromAL + (JoystickR.y / 3);
                    }
                    if (distanceFromAL < 1)
                    {
                        distanceFromAL = 1;
                    }
                }
                if (isPointing && !prevIsPointing) // Going from false to true
                {
                    CalculateAverageQuaternion();
                    CalaculatePostion();

                    if (activeWaterBlobInstance == null)
                    {
                        activeWaterBlobInstance = waterBlobObjectPool.GetWaterBlobInstance(spawnPosition, averageRotation);
                        waterBlobInstancesList.Add(activeWaterBlobInstance);
                    }
                    else
                    {
                        activeWaterBlobInstance.transform.position = spawnPosition;
                        activeWaterBlobInstance.transform.rotation = averageRotation;

                        float distance = Vector3.Distance(lefthandP, righthandP);
                        scaleFactor = distance * 100f;

                        // Apply scaling to the new object
                        activeWaterBlobInstance.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
                    }
                }
                else if (isPointing && prevIsPointing) // Going from true to true
                {
                    CalculateAverageQuaternion();
                    CalaculatePostion();

                    if (activeWaterBlobInstance != null)
                    {
                        activeWaterBlobInstance.transform.position = spawnPosition;
                        activeWaterBlobInstance.transform.rotation = averageRotation;

                        float distance = Vector3.Distance(lefthandP, righthandP);
                        scaleFactor = distance * 100f;

                        // Apply scaling to the new object
                        activeWaterBlobInstance.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
                    }
                }
                else if (!isPointing && prevIsPointing) // Going from true to false
                {
                    if (activeWaterBlobInstance != null)
                    {
                        waterBlobInstancesList.Add(activeWaterBlobInstance);
                        activeWaterBlobInstance = null;
                    }
                }
                if (activateFall())
                {
                    if (!isActivateFallPrev)
                    {
                        TempRight = righthandP;
                        TempLeft = lefthandP;
                    }
                    else if (HandFall())
                    {
                        DeleteAllWater();
                    }

                }


                isActivateFallPrev = activateFall();
                lefthandP = Player.Instance.leftControllerTransform.position;
                righthandP = Player.Instance.rightControllerTransform.position;
                lefthandR = Player.Instance.leftControllerTransform.rotation;
                righthandR = Player.Instance.rightControllerTransform.rotation;
            }
        }

        private bool HandFall()
        {
            return (TempLeft.z <= (lefthandP.z - 0.01f) && TempRight.z <= (righthandP.z - 0.01f));
        }
        bool activateFall()
        {
            return (rightTrigger && leftTrigger && !rightGrip && !leftGrip && secondaryR && secondaryL);
        }
        bool Pointing()
        {
            return (rightGrip && !rightTrigger)
                || (leftGrip && !leftTrigger);
        }
        public void DeleteAllWater()
        {
            if (waterBlobObjectPool != null)
            {
                waterBlobObjectPool.DeleteAllWater();
            }
        }

        private void CalaculatePostion()
        {
            Vector3 forwardDirection = averageRotation * Vector3.forward;

            averageDistance = (righthandP + lefthandP) / 2;
            forwardDirection.Normalize();

            Vector3 spawnOffset = forwardDirection * distanceFromAL;

            spawnPosition = averageDistance + spawnOffset;
        }

        private void CalculateAverageQuaternion()
        {
            Quaternion quaternion1 = righthandR.normalized;
            Quaternion quaternion2 = lefthandR.normalized;

            averageRotation = Quaternion.Slerp(quaternion1, quaternion2, 0.5f);
        }

        [ModdedGamemodeJoin]
        public void OnJoin(string gamemode)
        {
            inRoom = true;
        }

        [ModdedGamemodeLeave]
        public void OnLeave(string gamemode)
        {
            inRoom = false;
            DeleteAllWater();
        }
    }
}