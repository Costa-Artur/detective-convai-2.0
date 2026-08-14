using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Pistas sobre o crime
[CreateAssetMenu(fileName = "Clue", menuName = "Create Clue", order = 0)]
public class Clue : ScriptableObject
{
    public int id;
    //Número identificador da pista
    public string type;
    //Tipo da pista = ["suspeito","arma do crime","local"]
    public string evidenceName;
    //Nome da pista/evidencia = 'Senhorita Vermelho'
    public string evidenceText;
    //Texto a ser passado para o modelo de inteligência artificial
    public GameObject prefab;
    //GameObject da pista
}
