//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;

//public class GameStart : MonoBehaviour
//{
//    public static float cellSize = 2.7f; // Size of each cell in the grid
//    public static Rect gridBounds = new Rect(new Vector2(0, 0), new Vector2(8, 9) * cellSize + new Vector2(0.1f, 0.1f));
//    public static List<string> teams = new List<string>() { "BlueTeam", "RedTeam" };
//    public static List<Color> teamColors;
//    public static List<Material> teamMaterials;
//    public static List<GameObject> allUnits;

//    public static List<GameObject> blueUnits;
//    public static List<GameObject> redUnits;


//    [SerializeField] private GameObject wallPrefab;

//    [SerializeField] private GameObject unitCardsBlue;
//    [SerializeField] private GameObject unitCardsRed;

//    HashSet<Vector2Int> wallLayout = new HashSet<Vector2Int>()
//    {
//        new Vector2Int(1, 2),  // Row 2, Col 1
//        new Vector2Int(7, 3),  // Row 3, Col 7
//        new Vector2Int(8, 3),  // Row 3, Col 8
//        new Vector2Int(2, 4),  // Row 4, Col 2
//        new Vector2Int(6, 5),  // Row 5, Col 6
//        new Vector2Int(0, 6),  // Row 6, Col 0
//        new Vector2Int(1, 6),  // Row 6, Col 1
//        new Vector2Int(7, 7)   // Row 7, Col 7
//    };

//    // Start is called before the first frame update
//    void Start()
//    {
//        //Spawn in walls
//        foreach (var pos in wallLayout)
//        {
//            Vector3 worldPos = new Vector3(gridBounds.xMin + pos.x * cellSize, 0, gridBounds.yMin + pos.y * cellSize);
//            Instantiate(wallPrefab, worldPos, Quaternion.identity);
//        }
//        //Fill in unit cards
//        unitCardsBlue.SetActive(true);
//        unitCardsRed.SetActive(true);
//        for (int i = 0; i < blueUnits.Count; i++)
//        {
//            // Fill in blue unit cards
//            GameObject unitCard = unitCardsBlue.transform.GetChild(i).GetComponent<UnitCard>();
//            unitCard.SetUnit(blueUnits[i]);

//        }
//        for (int i = 0; i < redUnits.Count; i++)
//        {
//            // Fill in red unit cards
//        }

//        GameLoop.Instance.StartCoroutine(GameLoop.Instance.GameLoopTemp());
//    }

//    // Update is called once per frame
//    void Update()
//    {

//    }
//}
