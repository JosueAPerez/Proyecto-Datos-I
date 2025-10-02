using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;

/*
se va a encargar de manejar toda la parte de la UI de las cartas
es decir, las acciones que el jugador puede hacer con las cartas al jugar
se va a encargar de decir cuando fue clickeada y asi
*/
public class CartasUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI refs")]
    public Image cartaImage; //la imagen de la carta
    
    public GameObject highlight; //efecto de borde
    
    public UnityEngine.UI.Text txtTipo; //texto que dice el tipo de carta

    [HideInInspector] public Carta datosLogicos; //carga toda la logica de las cartas
    
    private bool seleccionada = false; //verifica si la carta fue seleccionada
    
    public Action<CartasUI> OnClicked; //por medio de un Action me dice si la CartaUI fue clickeada
    
    
    public void Configurar(Carta carta, Sprite spriteCarta)
    {
        datosLogicos = carta;
        if (cartaImage == null) cartaImage = GetComponentInChildren<Image>(); 
        MostrarSprite(spriteCarta);
        ActualizarTipoTexto();
        Deseleccionar();
        gameObject.SetActive(true);
    }

    public void MostrarSprite(Sprite s) //muestra el sprite en la UI
    {
        if (cartaImage == null) return;
        cartaImage.sprite = s;
        cartaImage.enabled = true;
    }

    public void Ocultar() //quita la carta de la UI 
    {
        if (cartaImage == null) return;
        cartaImage.enabled = false;
        datosLogicos = null;
        Deseleccionar();
    }

    
    public void OnClickCarta()
    {
        ToggleSeleccion();
        
        OnClicked?.Invoke(this);
    }

    //recibe infiormacion sobre si la carta fue clickeada en caso que lo fuera activa el OnClickOn
    public void OnPointerClick(PointerEventData eventData) => OnClickCarta();

    //encargado de altenar el valor de seleccion y activar el highligth si existe y si seleccionada es verad
    public void ToggleSeleccion()
    {
        seleccionada = !seleccionada;
        if (highlight != null) highlight.SetActive(seleccionada);
    }

    //se encarga de activar el highligth
    public void Seleccionar() { seleccionada = true; if (highlight != null) highlight.SetActive(true); }

    //se encarga de desactivar el highligth
    public void Deseleccionar() { seleccionada = false; if (highlight != null) highlight.SetActive(false); }

    //me dice si fue seleccionada
    public bool EstaSeleccionada() => seleccionada;

    //me actualiza el texto de la carta
    private void ActualizarTipoTexto()
    {
        if (txtTipo == null) return; //si el texto es nulo entonces no hace nada
        
        if (datosLogicos == null) { txtTipo.text = ""; return; } //si la carta es nula entonces no hace nada

        //ve cual es el tipo de la carta y actualiza el txttipo en base al cardtype
        switch (datosLogicos.tipo)
        {
            case CardType.Infanteria: txtTipo.text = "Infantería"; break;
            case CardType.Caballeria: txtTipo.text = "Caballería"; break;
            default: txtTipo.text = "Artillería"; break;
        }
    }
}
