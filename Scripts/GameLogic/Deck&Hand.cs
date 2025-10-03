using UnityEngine;
using System;
using System.Collections.Generic;

//clase de logica para manejar el mazo general de cartas
public class Mazo
{
    public MyArray<Territory> Territorios; //es la lista de los territorios
    public static MyArray<Carta> cartas { get; private set; } = new MyArray<Carta>(42); //es el mazo de todos
    public MyArray<CardType> tipos = new MyArray<CardType>(3); //son los tipos de cartas

    public static int CartasIntercambiadas; //me dice la cantidad de cartas que fueron intercambiadas
    public Mazo(MyArray<Territory> territorios)
    {
        //agrego los tipos de cartas
        tipos.Add(CardType.Infanteria);
        tipos.Add(CardType.Caballeria);
        tipos.Add(CardType.Artilleria);

        //creo la lista de territorios
        Territorios = territorios;
      
        //aun no hay cartas intercambias por eso se inicializa en 0
        CartasIntercambiadas = 0;
    }
  
    public void LlenarMazo() //me llena el mazo emperejando los tipos y los territorios para crear las cartas
    {
        for (int i = 0; i < cartas.Capacity; i++)
        {
            CardType tipo = tipos[i % 3]; //selecciona el tipo de carta

            //evalua el territorio a elegir, si la cantidad de territorios es menor a la de cartas
            //entonces escoge poner un null
            Territory terr = (i < Territorios.Count) ? Territorios[i] : null;

            //crea la carta y la adjenta al mazo
            cartas.Add(new Carta(tipo, Territorios[i]));
        }

        //mezcla el mazo
        Shuffle(cartas);
    }
  
    //roba una carta random del mazo y se la da al jugador
    public Carta RobarCarta()
    {
        if (cartas.Count == 0) return null;

        int idx_carta = UnityEngine.Random.Range(0, cartas.Count); //indice valido de carta random

        int idx_ocupado = cartas.Count - 1; //ultima posicion ocupada del mazo

        Carta cart = cartas[idx_carta]; //la carta random que vamos a dar

        //cambiamos el valor de la posicion de la carta random con la del ultimo espacio valida
        cartas[idx_carta] = cartas[idx_ocupado];  

        cartas.RemoveIdx(idx_ocupado); //removemos lel ultimo idx para evitar valores repetidos

        return cart; //devolvemos la carta
    }

    public void AgregarCarta(MyArray<Carta>carts) //para cuando las cartas se utilicen vuelvan al mazo
    {

        if (carts == null || carts.Count != 3) return;

        for (int i = 0; i < carts.Count; i++)
        {
            cartas.Add(carts[i]);
        }

        CartasIntercambiadas += carts.Count; //aumenta el numero de intarcambios
        if (CartasIntercambiadas >= 21) //establece un umbral de intercambios
        {
            Shuffle(cartas); //si se llega al numero del umbral entonces mezcla las cartas
            CartasIntercambiadas = 0; //reinicia el numero de cartas intercambiadas
        }
    }
    public void Shuffle<T>(MyArray<T> array) //logica para mezclar un array (lo hizo gpt)
    {
        int n = array.Count; //hasta donde esta lleno el array

        for (int i = n - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1); // índice aleatorio entre 0 e i
          
            // Intercambiar array[i] con array[j]
            T temp = array[i];
            array[i] = array[j];
            array[j] = temp;
        }
    }
  
    public int GetCount() => cartas.Count;
}

//Maneja toda la logica de lo que puede hacer la mano del jugador
public class ManoJugador
{
    public MyArray<Carta> hand { get; private set; } = new MyArray<Carta>(6); //la mano del jugador

    //agrega cartas a la mano siempre y cuando haya espacio y la carta exista
    public void AgregarCartaMano(Carta cart)
    {
        if (cart == null) { Debug.Log("La carta no existe (es null)"); return; }
        if (hand.Count < hand.Capacity)
        {
            hand.Add(cart);
        }

        else { Debug.Log("La hand esta llena"); return; }
    }

    /*
    verifica que las cartas que se seleccionarion para canjear
    sean canjeables
    */
    public bool VerificarSeleccion(MyArray<Carta>selec)
    {
        if (selec == null || selec.Count != 3) return false;

        //establece los tipos de cada carta
        CardType car1 = selec[0].tipo;
        CardType car2 = selec[1].tipo;
        CardType car3 = selec[2].tipo;
      
        //me dice si toas las cartas son iguales
        bool ver1 = (car1 == car2) && (car2 == car3);
        //me dice si toas las cartas son distintas
        bool ver2 = (car1 != car2) && (car2 != car3) && (car1 != car3);

        return ver1 || ver2;
    }
    /*
    realiza el intercambio de las cartas seleccionadas por tropas
    para eso pide las cartas seleeccionadas y verifica la seleccion
    y tambien pide la referencia del numero del canje a realizar (n_change)
    se usa un valor de referencia para poder cambiar una variable local y despues una global
    */
  
    public ulong Canjear(MyArray<Carta> selec, ref int n_change)
    {
        if (!VerificarSeleccion(selec)) //si no se puede canjear devuelve 0
        {
            Debug.Log("No se puede canjear");
            return 0;
        }

        //quita todas las caras de la hand del jugador
      
        for (int i = 0; i < selec.Count; i++)
        {
            hand.Remove(selec[i]);
        }

        n_change++; //aumenta el n_change

        return Fibo.Calc(n_change); //calcula el fibonacci para devolver la cantidad de tropas
    }
  
    public Carta ObtenerCartaiD(int id)
    {
        foreach (var carta in hand)
        {
            if (carta != null && carta.id == id)
            {
                return carta;
            }
        }
        return null;
    }
}

public static class Fibo
{
    private static ulong a = 2;
    private static ulong b = 3;

    private static int actual = 2;

    private const ulong Max_Tropas = 144UL;

    public static ulong Calc(int n)

    {
        if (n == 1) return 2;
        if (n == 2) return 3;

        if (n != actual + 1) throw new InvalidOperationException("Los canjes deben ser consecutivos");

        ulong res;

        if (ulong.MaxValue - b < a) res = Max_Tropas;
        else res = a + b;

        ulong final = (res > Max_Tropas) ? Max_Tropas : res;

        a = b;

        b = final;

        actual += 1;

        return final;

    }
    public static void Reset()
    {
        a = 2;
        b = 3;
        actual = 2;
    }

}
