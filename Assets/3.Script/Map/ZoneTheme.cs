using UnityEngine;

// 구역 하나의 배치 규칙. ZoneGenerator가 이 값으로 지형·바위·나무·문을 만든다. 값만 바꾸고 다시 생성하면 됨
[CreateAssetMenu(menuName = "NoeGeom/Zone Theme", fileName = "ZoneTheme")]
public class ZoneTheme : ScriptableObject
{
    public string zoneName = "Zone_1_Sanmun";
    public int seed = 1;
    // 구역 중심 위치. Generate All Zones가 구역 순서대로 북쪽(+Z)에 이어 붙이며 자동으로 채움
    public Vector3 origin;
    // HUD 구역 배너 문구 (예: "구역 1" / "산문")
    public string displayNumber = "";
    public string displayName = "";

    [Header("Arena")]
    public float combatRadius = 16f;   // 이 안은 장애물 없이 비워 둠
    public float wallRadius = 19f;     // 바위 벽과 투명 벽 위치
    public float gateHalfWidth = 3.5f; // 남(입구)·북(출구) 문 폭의 절반
    // 문이 나는 방향 (던전 허브 · 갈래 방용). 본편 구역은 남 · 북 그대로
    public bool gateSouth = true, gateNorth = true, gateEast, gateWest;

    // 난이도: 이 구역 웨이브 적의 체력 · 공격력 배율, 동시에 공격하는 적 수
    [Header("Difficulty")]
    public float strength = 1f;
    public int maxAttackers = 2;

    [Header("Terrain")]
    public float terrainSize = 140f;
    public float hillHeight = 10f;
    public float hillNoise = 4f;
    public float hillWidth = 22f;
    // 자연스러운 지형: 공터 가장자리 · 언덕 높이 · 폭을 노이즈로 흔들고(0 = 예전 매끈한 고리), 문 밖 길을 S자로 굽힘(m)
    public float hillRoughness = 0f;
    public float pathMeander = 0f;
    public float corridorFade = 6f; // 길 양옆 비탈 폭 (클수록 완만 · 길가는 투명 벽이 막음)
    public float valleyWidth = 0f;  // 길 양옆 이 폭 안의 언덕을 낮춰 협곡 대신 완만한 골짜기로 (0 = 끔)
    public TerrainLayer slopeLayer; // 가파른 비탈에 칠할 질감 (돌 등) · 늘어진 낙엽 질감을 가림
    public TerrainLayer groundLayer;
    public TerrainLayer arenaLayer;
    public TerrainLayer roadLayer;
    public TerrainLayer extraLayer; // 언덕 · 바깥 땅에 얼룩처럼 섞는 4번째 질감 (이끼 등)
    public float extraAmount = 0f;

    // 크기는 프리팹 원본 크기와 상관없이 이 범위로 맞춤 (바위는 가장 긴 변, 나무는 높이 기준)
    [Header("Wall Rocks")]
    public GameObject[] wallRocks;
    public Vector2 wallRockSize = new Vector2(4.5f, 7f);
    public float wallRockSpacing = 0.65f;

    [Header("Inner Rocks")]
    public GameObject[] innerRocks;
    public int innerRockCount = 8;
    public Vector2 innerRockSize = new Vector2(0.8f, 2f);

    [Header("Trees")]
    public GameObject[] trees;
    public Vector2 treeHeight = new Vector2(9f, 16f);
    public float treeMinDistance = 4.5f;
    public int treeMaxCount = 220;
    public int innerTreeCount = 0; // 전투 공간과 벽 사이에 심는 큰 나무 (공터 위로 가지가 덮임)

    [Header("Ground Cover")]
    public GameObject[] arenaCover;
    public int arenaCoverCount = 40;
    public GameObject[] outerCover;
    public int outerCoverCount = 80;
    public float outerCoverReach = 10f; // 벽 바깥 덤불을 심는 띠 폭 (실사 숲은 언덕까지 넓게)

    // 아레나 바깥 대각선 네 곳에 세우는 큰 구조물 (구역 3 폐사찰의 정자 등). 가장 긴 변 기준 크기
    [Header("Landmarks")]
    public GameObject[] landmarks;
    public float landmarkSize = 9f;
    public float landmarkDistance = 6f; // 벽 반지름 바깥으로 이만큼

    [Header("Path")]
    public GameObject[] pathStones;
    public float pathStoneSize = 1.6f;
    public float pathStoneSpacing = 2.4f;

    [Header("Atmosphere")]
    public Material skybox;
    public bool fog = true;
    public Color fogColor = new Color(0.72f, 0.8f, 0.82f);
    public float fogDensity = 0.008f;
    public Color sunColor = new Color(1f, 0.94f, 0.82f);
    public Vector3 sunEuler = new Vector3(45f, -35f, 0f);
    public GameObject[] groundParticles;
    public int groundParticleCount = 6;
    public GameObject[] overheadParticles;

    // [변경] 실사 숲 디테일: 지형 풀(GPU 인스턴싱) · 비탈 절벽 바위 · 반사 프로브 (0 · 비움 = 끔)
    [Header("Forest Detail")]
    public GameObject[] detailGrass;   // 지형 디테일로 촘촘히 까는 풀 (Unity Terrain용 프리팹)
    public GameObject[] detailPlants;  // 벽 바깥에 듬성듬성 섞는 고사리 · 들풀
    public float detailDensity = 0f;
    public float detailDistance = 60f;
    public GameObject[] slopeRocks;    // 가파른 비탈에 박는 흙벽 · 절벽 바위
    public int slopeRockCount = 0;
    public bool reflectionProbe;

    [Header("Gate")]
    public Color gateColor = new Color(0.72f, 0.12f, 0.1f);
    public Color barrierColor = new Color(0.45f, 0.75f, 1f, 0.35f);
    // [변경] 지정하면 상자 문 대신 기둥 · 관목 · 현판이 있는 도리이로 생성 (옻칠 · 검은 갓 · 받침돌)
    public Material gateMaterial;
    public Material gateTrimMaterial;
    public Material gateBaseMaterial;
}
