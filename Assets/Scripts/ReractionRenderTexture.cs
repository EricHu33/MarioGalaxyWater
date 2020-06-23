using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ReractionRenderTexture : MonoBehaviour
{
    public bool m_DisablePixelLights = true;
    public int m_TextureSize = 256;
    public float m_ClipPlaneOffset = 0.07f;
    public LayerMask m_RefractLayers = -1;
    private Dictionary<Camera, Camera> m_RefractionCameras = new Dictionary<Camera, Camera>(); // Camera -> Camera table
    private RenderTexture m_RefractionTexture = null;
    private int m_OldRefractionTextureSize = 0;
    private static bool s_InsideWater = false;
    private bool m_IsHardwareSupport;

    // This is called when it's known that the object will be rendered by some
    // camera. We render reflections / refractions and do other updates here.
    // Because the script executes in edit mode, reflections for the scene view
    // camera will just work!
    public void OnWillRenderObject()
    {
        if (!enabled || !GetComponent<Renderer>() || !GetComponent<Renderer>().sharedMaterial || !GetComponent<Renderer>().enabled)
            return;

        Camera cam = Camera.current;
        if (!cam)
            return;

        // Safeguard from recursive water reflections.		
        if (s_InsideWater)
            return;
        s_InsideWater = true;

        m_IsHardwareSupport = IsHardwareSupport();

        Camera refractionCamera;
        CreateOrUpdateRenderTexture(cam, out refractionCamera);

        // find out the reflection plane: position and normal in world space
        Vector3 pos = transform.position;
        Vector3 normal = transform.up;

        // Optionally disable pixel lights for reflection/refraction
        int oldPixelLightCount = QualitySettings.pixelLightCount;
        if (m_DisablePixelLights)
            QualitySettings.pixelLightCount = 0;
        UpdateCameraModes(cam, refractionCamera);

        // Render refraction
        if (m_IsHardwareSupport)
        {
            refractionCamera.worldToCameraMatrix = cam.worldToCameraMatrix;

            // Setup oblique projection matrix so that near plane is our reflection
            // plane. This way we clip everything below/above it for free.
            Vector4 clipPlane = CameraSpacePlane(refractionCamera, pos, normal, -1.0f);
            refractionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);
            //refractionCamera.projectionMatrix = cam.projectionMatrix;

            refractionCamera.cullingMask = ~(1 << 4) & m_RefractLayers.value; // never render water layer
            refractionCamera.targetTexture = m_RefractionTexture;
            refractionCamera.transform.position = cam.transform.position;
            refractionCamera.transform.rotation = cam.transform.rotation;
            refractionCamera.Render();
            GetComponent<Renderer>().sharedMaterial.SetTexture("_RefractionTex", m_RefractionTexture);
        }

        // Restore pixel light count
        if (m_DisablePixelLights)
            QualitySettings.pixelLightCount = oldPixelLightCount;

        // Setup shader keywords based on water mode
        if (m_IsHardwareSupport)
        {
            Shader.EnableKeyword("WATER_REFRACTIVE");
        }

        s_InsideWater = false;
    }


    // Cleanup all the objects we possibly have created
    void OnDisable()
    {
        if (m_RefractionTexture)
        {
            DestroyImmediate(m_RefractionTexture);
            m_RefractionTexture = null;
        }

        foreach (KeyValuePair<Camera, Camera> kvp in m_RefractionCameras)
            DestroyImmediate((kvp.Value).gameObject);
        m_RefractionCameras.Clear();
    }


    // This just sets up some matrices in the material; for really
    // old cards to make water texture scroll.
    void Update()
    {
        /*if (!GetComponent<Renderer>())
            return;
        Material mat = GetComponent<Renderer>().sharedMaterial;
        if (!mat)
            return;

        Vector4 waveSpeed = mat.GetVector("WaveSpeed");
        float waveScale = mat.GetFloat("_WaveScale");
        Vector4 waveScale4 = new Vector4(waveScale, waveScale, waveScale * 0.4f, waveScale * 0.45f);

        // Time since level load, and do intermediate calculations with doubles
        double t = Time.timeSinceLevelLoad / 20.0;
        Vector4 offsetClamped = new Vector4(
            (float)System.Math.IEEERemainder(waveSpeed.x * waveScale4.x * t, 1.0),
            (float)System.Math.IEEERemainder(waveSpeed.y * waveScale4.y * t, 1.0),
            (float)System.Math.IEEERemainder(waveSpeed.z * waveScale4.z * t, 1.0),
            (float)System.Math.IEEERemainder(waveSpeed.w * waveScale4.w * t, 1.0)
        );

        mat.SetVector("_WaveOffset", offsetClamped);
        mat.SetVector("_WaveScale4", waveScale4);

        Vector3 waterSize = GetComponent<Renderer>().bounds.size;
        Vector3 scale = new Vector3(waterSize.x * waveScale4.x, waterSize.z * waveScale4.y, 1);
        Matrix4x4 scrollMatrix = Matrix4x4.TRS(new Vector3(offsetClamped.x, offsetClamped.y, 0), Quaternion.identity, scale);
        mat.SetMatrix("_WaveMatrix", scrollMatrix);

        scale = new Vector3(waterSize.x * waveScale4.z, waterSize.z * waveScale4.w, 1);
        scrollMatrix = Matrix4x4.TRS(new Vector3(offsetClamped.z, offsetClamped.w, 0), Quaternion.identity, scale);
        mat.SetMatrix("_WaveMatrix2", scrollMatrix);*/
    }

    private void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null)
            return;
        // set water camera to clear the same way as current camera
        dest.clearFlags = src.clearFlags;
        dest.backgroundColor = src.backgroundColor;
        if (src.clearFlags == CameraClearFlags.Skybox)
        {
            Skybox sky = src.GetComponent(typeof(Skybox)) as Skybox;
            Skybox mysky = dest.GetComponent(typeof(Skybox)) as Skybox;
            if (!sky || !sky.material)
            {
                mysky.enabled = false;
            }
            else
            {
                mysky.enabled = true;
                mysky.material = sky.material;
            }
        }
        // update other values to match current camera.
        // even if we are supplying custom camera&projection matrices,
        // some of values are used elsewhere (e.g. skybox uses far plane)
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
    }

    private void CreateOrUpdateRenderTexture(Camera currentCamera, out Camera refractionCamera)
    {
        refractionCamera = null;

        if (m_IsHardwareSupport)
        {
            // Refraction render texture
            if (!m_RefractionTexture || m_OldRefractionTextureSize != m_TextureSize)
            {
                if (m_RefractionTexture)
                    DestroyImmediate(m_RefractionTexture);
                m_RefractionTexture = new RenderTexture(m_TextureSize, m_TextureSize, 16);
                m_RefractionTexture.name = "_Refraction" + GetInstanceID();
                m_RefractionTexture.isPowerOfTwo = true;
                m_RefractionTexture.hideFlags = HideFlags.DontSave;
                m_OldRefractionTextureSize = m_TextureSize;
            }

            // Camera for refraction
            m_RefractionCameras.TryGetValue(currentCamera, out refractionCamera);
            if (!refractionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
            {
                GameObject go = new GameObject("Water Refr Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera), typeof(Skybox));
                refractionCamera = go.GetComponent<Camera>();
                refractionCamera.enabled = false;
                refractionCamera.transform.position = transform.position;
                refractionCamera.transform.rotation = transform.rotation;
                refractionCamera.gameObject.AddComponent<FlareLayer>();
                go.hideFlags = HideFlags.DontSave;
                m_RefractionCameras[currentCamera] = refractionCamera;
            }
        }
    }


    private bool IsHardwareSupport()
    {
        if (!SystemInfo.supportsRenderTextures || !GetComponent<Renderer>())
            return false;

        Material mat = GetComponent<Renderer>().sharedMaterial;
        if (!mat)
            return false;

        return true;
    }

    // Extended sign: returns -1, 0 or 1 based on sign of a
    private static float sgn(float a)
    {
        if (a > 0.0f) return 1.0f;
        if (a < 0.0f) return -1.0f;
        return 0.0f;
    }

    // Given position/normal of the plane, calculates plane in camera space.
    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * m_ClipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    // Calculates reflection matrix around the given plane
    private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
        reflectionMat.m01 = (-2F * plane[0] * plane[1]);
        reflectionMat.m02 = (-2F * plane[0] * plane[2]);
        reflectionMat.m03 = (-2F * plane[3] * plane[0]);

        reflectionMat.m10 = (-2F * plane[1] * plane[0]);
        reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
        reflectionMat.m12 = (-2F * plane[1] * plane[2]);
        reflectionMat.m13 = (-2F * plane[3] * plane[1]);

        reflectionMat.m20 = (-2F * plane[2] * plane[0]);
        reflectionMat.m21 = (-2F * plane[2] * plane[1]);
        reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
        reflectionMat.m23 = (-2F * plane[3] * plane[2]);

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }
}
