using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField inputNombre;
    public TMP_InputField inputIP;
    public Button botonHost;
    public Button botonClient;
    public Button botonServer;

    [Header("Config")]
    public string gameSceneName = "GameScene";
    public ushort port = 7777;

    void Start()
    {
        botonHost.onClick.AddListener(OnHostClick);
        botonClient.onClick.AddListener(OnClientClick);
        botonServer.onClick.AddListener(OnServerClick);
    }

    void OnHostClick()
    {
        GuardarNombre();
        ConfigurarTransporte("0.0.0.0"); // escucha en todas las interfaces
        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log("✅ Host iniciado");
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.LogError("❌ No se pudo iniciar Host");
        }
    }

    void OnClientClick()
    {
        GuardarNombre();
        if (string.IsNullOrEmpty(inputIP.text))
        {
            Debug.LogWarning("⚠️ Ingresa la IP del servidor.");
            return;
        }

        ConfigurarTransporte(inputIP.text);
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("✅ Cliente iniciado");
            // Los clientes no cargan escenas manualmente, Netcode lo hace
        }
        else
        {
            Debug.LogError("❌ No se pudo iniciar Cliente");
        }
    }

    void OnServerClick()
    {
        GuardarNombre();
        ConfigurarTransporte("0.0.0.0");
        if (NetworkManager.Singleton.StartServer())
        {
            Debug.Log("✅ Servidor iniciado");
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.LogError("❌ No se pudo iniciar Servidor");
        }
    }

    void GuardarNombre()
    {
        string nombre = inputNombre.text.Trim();
        if (!string.IsNullOrEmpty(nombre))
            GameSettings.NombreJugador = nombre;
    }

    void ConfigurarTransporte(string ip)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.ConnectionData.Address = ip;
            transport.ConnectionData.Port = port;
        }
    }
}
