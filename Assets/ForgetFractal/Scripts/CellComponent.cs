using UnityEngine;

public class CellComponent : MonoBehaviour {
	public const int MAX_DEPTH = 10;

	public static readonly Color MULTICOLOR = Color.cyan;
	public static readonly Vector3 SIZE = new Vector3(0.015f, 0.005f, 0.015f);

	public static readonly Color[] COLORS = new[] { Color.red, Color.green, Color.magenta, Color.yellow, Color.blue, Color.cyan };

	public GameObject CubePrefab;
	public KMSelectable Selectable;
	public Renderer Renderer;

	private Color _color = Color.black; public Color Color { get { return _color; } set { if (_color == value) return; _color = value; UpdateColor(); } }
	private GameObject[] _multicolors = null;

	private void Start() {
		UpdateColor();
	}

	private void UpdateColor() {
		Renderer.material.color = _color;
		if (_color == MULTICOLOR) {
			if (_multicolors != null) return;
			int clInd = Random.Range(5, 8) % COLORS.Length;
			Renderer.gameObject.SetActive(false);
			_multicolors = new GameObject[MAX_DEPTH];
			Vector3 offset = Vector3.zero;
			Vector3 size = SIZE;
			for (int i = 0; i < MAX_DEPTH; i++) {
				GameObject cube = Instantiate(CubePrefab);
				_multicolors[i] = cube;
				cube.transform.parent = transform;
				Vector3 selfOffset = Vector3.zero;
				if (i + 1 < MAX_DEPTH) {
					if (i % 2 == 0) {
						size.z /= 2f;
						selfOffset.z += size.z / 2f * (Random.Range(0, 2) == 0 ? 1 : -1);
					} else {
						size.x /= 2f;
						selfOffset.x += size.x / 2f * (Random.Range(0, 2) == 0 ? 1 : -1);
					}
				}
				cube.transform.localPosition = offset + selfOffset;
				cube.transform.localScale = size;
				cube.transform.localRotation = Quaternion.identity;
				cube.GetComponent<Renderer>().material.color = COLORS[clInd];
				offset -= selfOffset;
				clInd = (clInd + Random.Range(1, 3)) % COLORS.Length;
			}
		} else {
			if (_multicolors == null) return;
			Renderer.gameObject.SetActive(true);
			foreach (GameObject obj in _multicolors) Destroy(obj);
			_multicolors = null;
		}
	}
}
