using System;
using System.Collections;
using System.Collections.Generic;
public class MyArray<T> : IEnumerable<T>
{
    private T[] items;
    private int idx_ocupado = -1;

    public MyArray(int size)
    {
        items = new T[size];
    }

    public int Count => idx_ocupado + 1; //dice hasta donde verdaderamente esta lleno el array
    public int Capacity => items.Length; //es el total de elementos que permite el array

    public bool EstaLleno => Count == Capacity; //dice si el array esta totalmente lleno
    private void ValidateIndex(int index) //valida si el indice esta dentro del rango permitido
    {
        if (index < 0 || index > idx_ocupado) throw new System.IndexOutOfRangeException();
    }

    public T this[int index] //indexador
    {
        get
        {
            ValidateIndex(index);

            return items[index];
        }
        set
        {
            ValidateIndex(index);

            items[index] = value;
        }
    }

    public void Add(T item) //agregar un elemento al array
    {
        if (EstaLleno) //verifica que el indice este dentro del rango
        {
            throw new System.InvalidOperationException("El array está lleno"); //tira una excepcion si el array esta lleno
        }

        idx_ocupado++; //aumenta la posicion del indice de los ocupados
        items[idx_ocupado] = item; //agrega el territorio en dicha posicion
    }

    public bool Contains(T item) //dice si esta el elemento en el array
    {

        for (int i = 0; i <= idx_ocupado; i++) //ciclo que pasa por todas las posiciones ocupadas
        {
            if (Equals(items[i], item)) return true; //devuelve true si hay una coincidencia
        }

        return false; //devuelve false si la lista esta vacia (idx_ocupado = -1) o porque no hay coincidencias
    }
    public void Remove(T item)
    {
        int idx = -1; //se inicializa un indice

        for (int i = 0; i <= idx_ocupado; i++) //ciclo que pasa por todas las posiciones ocupadas
        {
            if (Equals(items[i], item)) //verifica coincidencias
            {
                idx = i; //hace que el idx sea el idx del que queremos eliminar
                break;
            }
        }

        if (idx == -1) return; //si idx = -1 es porque el valor no esta en la lista entonces no hace nada

        for (int j = idx; j < idx_ocupado; j++) //se pasa por todos los valores ocupados desde el indice que queremos eliminar
        {
            items[j] = items[j + 1]; //desplaza a todos los elementos despues del que queremos eliminar
        }

        items[idx_ocupado] = default; //hace que el ultimo espacio se ponga en el valor default del tipo T
        idx_ocupado--; //dice que ahora el anterior es el ultimo ocupado
    }

    public void RemoveIdx(int idx) //lo mismo que el remove pero con indices
    {
        ValidateIndex(idx);

        for (int i = idx; i < idx_ocupado; i++)
        {
            items[i] = items[i + 1];
        }

        items[idx_ocupado] = default;
        idx_ocupado--;
    }
    // a partir de aqui es la logica para poder hacer foreach
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i <= idx_ocupado; i++)
            yield return items[i];
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}


