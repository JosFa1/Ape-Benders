using BepInEx;
using System;
using UnityEngine;
using UnityEngine.XR;
using Utilla;
using GorillaLocomotion;
using GorillaLocomotion.Swimming;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

namespace Ape_Bending
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

            // Check if there is an inactive water blob instance in the pool
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

            // If there are no inactive instances, create a new one
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
    }

    /* This attribute tells Utilla to look for [ModdedGameJoin] and [ModdedGameLeave] */
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
        private bool waterPlacing = false;
        private Quaternion righthandR;
        private Quaternion lefthandR;
        private Vector3 righthandP;
        private Vector3 lefthandP;
        private Quaternion averageRotation;
        private Vector3 averageDistance;
        private XRNode leftHandNode = XRNode.LeftHand;
        private XRNode rightHandNode = XRNode.RightHand;

        private WaterBlobObjectPool waterBlobObjectPool; // Add this line

        void Start()
        {
            /* A lot of Gorilla Tag systems will not be set up when start is called */
            /* Put code in OnGameInitialized to avoid null references */

            Utilla.Events.GameInitialized += OnGameInitialized;
        }

        void OnEnable()
        {
            /* Set up your mod here */
            /* Code here runs at the start and whenever your mod is enabled*/

            HarmonyPatches.ApplyHarmonyPatches();
        }

        void OnDisable()
        {
            /* Undo mod setup here */
            /* This provides support for toggling mods with ComputerInterface, please implement it :) */
            /* Code here runs whenever your mod is disabled (including if it disabled on startup)*/

            HarmonyPatches.RemoveHarmonyPatches();
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
                Console.WriteLine("======================================================");
                var bundle = LoadAssetBundle("Frozone.Resources.apebendingassets");
                foreach (var name in bundle.GetAllAssetNames())
                {
                    Console.WriteLine(name);
                }
                GameObject waterBlobPrefab = bundle.LoadAsset<GameObject>("water blob");
                waterBlobPrefab.SetActive(false);
                waterBlobPrefab.AddComponent<GorillaLocomotion.Swimming.WaterVolume>();
                waterBlobPrefab.AddComponent<WaterSurfaceMaterialController>();

                // Create the water blob object pool
                waterBlobObjectPool = new WaterBlobObjectPool(waterBlobPrefab);
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
                InputDevices.GetDeviceAtXRNode(leftHandNode).TryGetFeatureValue(CommonUsages.gripButton, out leftGrip);
                InputDevices.GetDeviceAtXRNode(rightHandNode).TryGetFeatureValue(CommonUsages.gripButton, out rightGrip);
                InputDevices.GetDeviceAtXRNode(leftHandNode).TryGetFeatureValue(CommonUsages.triggerButton, out leftTrigger);
                InputDevices.GetDeviceAtXRNode(rightHandNode).TryGetFeatureValue(CommonUsages.triggerButton, out rightTrigger);
                bool isPointing = Pointing();
                Debug.Log(isPointing);

                if (isPointing)
                {
                    waterPlacing = true;
                    if (waterPlacing && (rightGrip || leftGrip))
                    {
                        CalculateAverageQuaternion();
                        float distanceFromAL = 1.0f; // Set the desired distance from the averageLocation

                        // Calculate the spawn position in the direction of the averageRotation
                        Vector3 spawnPosition = averageDistance + averageRotation * Vector3.forward * distanceFromAL;

                        // Spawn the water blob at the calculated position and rotation
                        GameObject newWaterBlob = waterBlobObjectPool.GetWaterBlobInstance(spawnPosition, averageRotation);
                    }
                    else
                    {
                        waterPlacing = false;
                    }
                }
                lefthandP = Player.Instance.leftControllerTransform.position;
                righthandP = Player.Instance.rightControllerTransform.position;
                lefthandR = Player.Instance.leftControllerTransform.rotation;
                righthandR = Player.Instance.rightControllerTransform.rotation;
            }
            // Use the 'isPointing' variable as needed
        }


        bool Pointing()
        {
            return (rightGrip && !rightTrigger && Vector3.Dot(Vector3.forward, righthandR * Vector3.forward) > 0)
                || (leftGrip && leftTrigger && Vector3.Dot(Vector3.forward, lefthandR * Vector3.forward) > 0);
        }


        private void CalculateAverageQuaternion()
        {
            Quaternion quaternion1 = righthandR.normalized;
            Quaternion quaternion2 = lefthandR.normalized;

            // Use slerp to interpolate between the two quaternions
            averageRotation = Quaternion.Slerp(quaternion1, quaternion2, 0.5f);
        }

        /* This attribute tells Utilla to call this method when a modded room is joined */
        [ModdedGamemodeJoin]
        public void OnJoin(string gamemode)
        {
            /* Activate your mod here */
            /* This code will run regardless of if the mod is enabled*/

            inRoom = true;
        }

        /* This attribute tells Utilla to call this method when a modded room is left */
        [ModdedGamemodeLeave]
        public void OnLeave(string gamemode)
        {
            /* Deactivate your mod here */
            /* This code will run regardless of if the mod is enabled*/

            inRoom = false;

            // Clear the object pool
            waterBlobObjectPool.Clear();
        }
    }
}
