using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Refs UI")]
    public TextMeshProUGUI PlayerName;
    public TextMeshProUGUI tropasDisponiblesText;
    public Button cambiarFaseButton;
    public TextMeshProUGUI FaseActual;
    public TextMeshProUGUI tropasEnTerritorioText;
    public TextMeshProUGUI sliderValueText;
    public Slider tropasSlider;
    public Button colocarButton;
    public Button terminarTurnoButton;

    [Header("Backgrounds por fase")]
    public Camera mainCamera;
    public Color colorRefuerzo = Color.green;
    public Color colorAtaque = Color.red;
    public Color colorReagrupacion = Color.blue;

    [Header("Inventario de Cartas")]
    public GameObject inventarioPanel;
    public GameObject cartaPrefab;
    public Transform contenedorCartas;
    public Button inventarioToggleButton;
    public Button confirmarIntercambioButton;

    [Header("Sprites de cartas")]
    public Sprite spriteInfanteria;
    public Sprite spriteCaballeria;
    public Sprite spriteArtilleria;

    [Header("Defense Prompt UI")]
    public GameObject defensePromptPanel;
    public Slider defensaSlider;
    public TextMeshProUGUI defensaSliderValueText;
    public TextMeshProUGUI defensaPromptTitle;

    [Header("Card UI Prefab")]
    public GameObject cartaUIPrefab; // para ManoJugadorUI prefabs si no están asignados en players

    private Territory territorioSeleccionado;
    private bool inventarioAbierto = false;
    private int pendingAttackIdForUI = -1;
    private int pendingAtacanteIdxForUI = -1;
    private int pendingDefensorIdxForUI = -1;
    private int pendingTropasAtqForUI = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        RefrescarUI();

        if (colocarButton != null) colocarButton.onClick.AddListener(OnColocarTropas);
        if (terminarTurnoButton != null) terminarTurnoButton.onClick.AddListener(OnTerminarTurno);
        if (tropasSlider != null) tropasSlider.onValueChanged.AddListener(delegate { OnSliderChanged(); });
        if (cambiarFaseButton != null) cambiarFaseButton.onClick.AddListener(OnCambiarFase);
        if (inventarioToggleButton != null) inventarioToggleButton.onClick.AddListener(ToggleInventario);
        if (confirmarIntercambioButton != null) confirmarIntercambioButton.onClick.AddListener(OnConfirmarIntercambio);

        defensePromptPanel?.SetActive(false);
        inventarioPanel?.SetActive(false);

        if (BoardManager.Instance != null)
        {
            BoardManager.Instance.JugadorActualIdx.OnValueChanged += (_, __) => RefrescarUI();
            BoardManager.Instance.FaseActual.OnValueChanged += (_, __) => { ActualizarBackground(); RefrescarUI(); };
        }
    }

    public void SeleccionarTerritorio(Territory terr)
    {
        territorioSeleccionado = terr;
        RefrescarUI();
    }

    public void RefrescarUI()
    {
        var jugador = BoardManager.Instance?.GetJugadorLocal();
        if (jugador == null) return;

        PlayerName.text = jugador.Alias.Value.ToString();
        tropasDisponiblesText.text = $"Tropas disponibles: {jugador.TropasDisponibles.Value}";
        FaseActual.text = $"Fase actual: {BoardManager.Instance.FaseActual.Value}";

        if (territorioSeleccionado != null && jugador.ComprobarTerritorio(territorioSeleccionado))
        {
            tropasEnTerritorioText.text = $"Tropas en territorio: {territorioSeleccionado.Soldiers.Value}";

            switch (BoardManager.Instance.FaseActual.Value)
            {
                case BoardManager.TurnPhase.Refuerzo:
                    tropasSlider.minValue = 1;
                    tropasSlider.maxValue = Mathf.Max(1, jugador.TropasDisponibles.Value);
                    break;
                case BoardManager.TurnPhase.Ataque:
                    int maxT = Mathf.Max(0, territorioSeleccionado.Soldiers.Value - 1);
                    tropasSlider.minValue = maxT > 0 ? 1 : 0;
                    tropasSlider.maxValue = Mathf.Min(3, maxT); // atacante max 3
                    break;
                case BoardManager.TurnPhase.Reagrupacion:
                    int maxT2 = Mathf.Max(0, territorioSeleccionado.Soldiers.Value - 1);
                    tropasSlider.minValue = maxT2 > 0 ? 1 : 0;
                    tropasSlider.maxValue = maxT2;
                    break;
            }

            if (tropasSlider.maxValue < tropasSlider.minValue) tropasSlider.value = 0;
            else tropasSlider.value = tropasSlider.minValue;

            OnSliderChanged();
        }
        else
        {
            tropasEnTerritorioText.text = "Tropas en territorio: -";
            sliderValueText.text = "";
        }
    }

    public void ActualizarBackground()
    {
        if (BoardManager.Instance == null) return;
        switch (BoardManager.Instance.FaseActual.Value)
        {
            case BoardManager.TurnPhase.Refuerzo: mainCamera.backgroundColor = colorRefuerzo; break;
            case BoardManager.TurnPhase.Ataque: mainCamera.backgroundColor = colorAtaque; break;
            case BoardManager.TurnPhase.Reagrupacion: mainCamera.backgroundColor = colorReagrupacion; break;
        }
    }

    private void OnSliderChanged()
    {
        if (sliderValueText != null && tropasSlider != null) sliderValueText.text = $"{(int)tropasSlider.value}";
        if (defensaSliderValueText != null && defensaSlider != null) defensaSliderValueText.text = $"{(int)defensaSlider.value}";
    }

    private void OnCambiarFase()
    {
        if (BoardManager.Instance != null) BoardManager.Instance.CambiarFaseServerRpc();
        RefrescarUI();
    }

    private void OnColocarTropas()
    {
        var jugador = BoardManager.Instance.GetJugadorLocal();
        if (jugador == null || territorioSeleccionado == null) return;
        int cantidad = (int)tropasSlider.value;
        if (jugador.ColocarTropas(territorioSeleccionado, cantidad)) RefrescarUI();
    }

    private void OnTerminarTurno()
    {
        if (BoardManager.Instance != null) BoardManager.Instance.TerminarTurnoServerRpc();
        territorioSeleccionado = null;
        tropasEnTerritorioText.text = "Tropas en territorio: -";
        sliderValueText.text = "";
        RefrescarUI();
    }

    #region Defense prompt (defensor selecciona 1..2)
    public void ShowDefensePrompt(int attackId, int atacanteIdx, int defensorIdx, int tropasAtq, int maxDefPossible)
    {
        if (defensePromptPanel == null || defensaSlider == null || defensaSliderValueText == null || defensaPromptTitle == null)
        {
            Debug.LogWarning("ShowDefensePrompt: UI references no asignadas.");
            return;
        }

        pendingAttackIdForUI = attackId;
        pendingAtacanteIdxForUI = atacanteIdx;
        pendingDefensorIdxForUI = defensorIdx;
        pendingTropasAtqForUI = tropasAtq;

        defensePromptPanel.SetActive(true);
        defensaSlider.minValue = 1;
        defensaSlider.maxValue = Mathf.Max(1, maxDefPossible);
        defensaSlider.value = defensaSlider.minValue;
        OnSliderChanged();
        defensaPromptTitle.text = $"Atacan desde {atacanteIdx} con {tropasAtq} tropas. Elige tropas para defender:";
    }

    public void OnConfirmDefenseButton()
    {
        if (pendingAttackIdForUI < 0)
        {
            Debug.LogWarning("No hay ataque pendiente en UI.");
            return;
        }
        int chosen = (int)defensaSlider.value;
        BoardManager.Instance?.DefenderResponseServerRpc(pendingAttackIdForUI, chosen);
        defensePromptPanel.SetActive(false);
        pendingAttackIdForUI = -1;
    }
    #endregion

    #region Inventory / cards UI
    private void ToggleInventario()
    {
        inventarioAbierto = !inventarioAbierto;
        inventarioPanel.SetActive(inventarioAbierto);
        if (inventarioAbierto) ActualizarInventario(BoardManager.Instance.GetJugadorLocal());
    }

    public void ActualizarInventario(NetworkPlayer jugador)
    {
        if (jugador == null) return;
        foreach (Transform child in contenedorCartas) GameObject.Destroy(child.gameObject);

        for (int i = 0; i < jugador.Mano.mano.Count; i++)
        {
            Carta carta = jugador.Mano.mano[i];
            GameObject nueva = Instantiate(cartaPrefab, contenedorCartas);
            Image img = nueva.GetComponent<Image>();
            switch (carta.tipo)
            {
                case CardType.Infanteria: img.sprite = spriteInfanteria; break;
                case CardType.Caballeria: img.sprite = spriteCaballeria; break;
                default: img.sprite = spriteArtilleria; break;
            }
            TextMeshProUGUI label = nueva.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = carta.territorio != null ? $"ID {carta.territorio.Idx}" : "";
        }
    }

    private void OnConfirmarIntercambio()
    {
        var jugador = BoardManager.Instance.GetJugadorLocal();
        if (jugador == null) return;
        // UI de selección de 3 cartas no implementada aquí; se delega a ManoJugadorUI en el prefab del player
        Debug.Log("OnConfirmarIntercambio: si usas ManoJugadorUI, el canje se realiza desde ahí.");
    }
    #endregion

    #region Attack result handling (recibido desde BoardManager)
    public void HandleAttackResult(BoardManager.AttackResultSummary summary)
    {
        string s = $"Ataque {summary.attackId}: {summary.atacanteIdx} vs {summary.defensorIdx}\n" +
                   $"Dados Atq: {string.Join(",", summary.attRolls ?? new int[0])}  Dados Def: {string.Join(",", summary.defRolls ?? new int[0])}\n" +
                   $"Pérdidas Atq: {summary.attackerLosses}  Pérdidas Def: {summary.defenderLosses}\n" +
                   (summary.conquered ? $"¡Conquistado! Tropas movidas: {summary.tropasMovidas}" : "Territorio defendido");
        Debug.Log("[UI] Resultado de ataque:\n" + s);
        RefrescarUI();
    }
    #endregion
}
