using Unity.Netcode;
using Unity.Collections;

//permite volver la informacion de las cartas en serializables
//ocasionando un mejor envio de informacion para los traslados RPC
public struct CardData : INetworkSerializable
{
    //informacion de la carta de manera compacta
    public int territoryIdx; //indice del territorio que pertenece, si es -1 entonces no se ligo a un territorio
    public int tipo; //me dice el indice del enum (0, 1, 2) para saber el tipo de carta

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        //serializa los valores, el ref me permite hacer referencia a tal valor
        //para que esta sufra el cambio
        serializer.SerializeValue(ref territoryIdx); 
        serializer.SerializeValue(ref tipo);
    }
}
//estructura para el envio de informacion de las cartas seleccionadas
public struct CardSelection : INetworkSerializable
{
    public int idx0, idx1, idx2; //los indices del enum CardType
    public int tipo0, tipo1, tipo2; //Los tipos de cartas que hay

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        //serializa todos los valores de los indices y tipos
        serializer.SerializeValue(ref idx0);
        serializer.SerializeValue(ref idx1);
        serializer.SerializeValue(ref idx2);
        serializer.SerializeValue(ref tipo0);
        serializer.SerializeValue(ref tipo1);
        serializer.SerializeValue(ref tipo2);
    }
}
//estructura para tener la informacion de las cartas que fueron canjeadas
public struct RemovedCardsInfo : INetworkSerializable
{
    public int count;
    public int idx0, idx1, idx2;
    public int tipo0, tipo1, tipo2;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref count);
        serializer.SerializeValue(ref idx0);
        serializer.SerializeValue(ref idx1);
        serializer.SerializeValue(ref idx2);
        serializer.SerializeValue(ref tipo0);
        serializer.SerializeValue(ref tipo1);
        serializer.SerializeValue(ref tipo2);
    }
    //metodos para obtener el indice y el tipo de cartas removidas
    public int GetIdxAt(int i)
    {
        switch (i)
        {
            case 0: return idx0;
            case 1: return idx1;
            case 2: return idx2;
            default: return -1;
        }
    }

    public int GetTipoAt(int i)
    {
        switch (i)
        {
            case 0: return tipo0;
            case 1: return tipo1;
            case 2: return tipo2;
            default: return 0;
        }
    }
}
