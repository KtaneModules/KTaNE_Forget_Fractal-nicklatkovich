using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;
using KeepCoding;

public class ForgetFractalModule : KtaneModule {
	public const float CELLS_INTERVAL = 0.018f;
	public const float CELLS_EXTRA_INTERVAL = 0.001f;
	public const float ANIMATION_DURATION = 1f;

	public static readonly Color BLACK = Color.black;

	private enum State { NOT_ACTIVATED, READ, READY, SUBMIT, RECOVERY, SOLVED }

	private const int CYAN_INDEX = 3;
	private const int MAGENTA_INDEX = 4;
	private const int YELLOW_INDEX = 5;
	private const string COLORS_NAME = "RGMYB?";

	public Transform GridContainer;
	public TextMesh StageText;
	public KMSelectable SelfSelectable;
	public KMSelectable Screen;
	public KMAudio Audio;
	public KMBossModule BossModule;
	public CellComponent CellPrefab;

	private bool _displaySwapsCount = false;
	private int _requiredSwapsCount;
	private int _stagesCount;
	private int _cellsPerStage;
	private int _displayedStageIndex = -1;
	private int _submitionsCount = 0;
	private int[] _fractal;
	private int[] _submition;
	private int[] _decoder;
	private int[] _colorsMap;
	private int[][] _stagesCells;
	private int[][] _stagesColors;
	private State _state = State.NOT_ACTIVATED;
	private CellComponent[] _cells = new CellComponent[32];
	private HashSet<string> _ignoredModules;
	private Coroutine _animation = null;

	protected override void Start() {
		base.Start();
		for (int i = 0; i < 32; i++) {
			CellComponent cell = Instantiate(CellPrefab);
			cell.transform.parent = GridContainer;
			cell.transform.localScale = Vector3.one;
			cell.transform.localRotation = Quaternion.identity;
			Vector3 pos = Vector3.zero;
			for (int group = 0; group < 5; group++) {
				int groupSize = 32 >> group;
				int extra = (4 - group) / 2;
				extra *= extra;
				float diff = CELLS_INTERVAL * 2f / (1 << ((group + 1) / 2)) + CELLS_EXTRA_INTERVAL * extra;
				if (i % groupSize < groupSize / 2) diff = -diff;
				if (group % 2 == 0) pos.x += diff;
				else pos.z -= diff;
			}
			cell.transform.localPosition = pos;
			cell.Selectable.Parent = SelfSelectable;
			_cells[i] = cell;
		}
		SelfSelectable.Children = new[] { Screen }.Concat(_cells.Select(cell => cell.Selectable)).ToArray();
		SelfSelectable.UpdateChildrenProperly();
		StageText.text = "FRACTAL";
		IEnumerable<int> fakeExtraColors = Enumerable.Range(2, 3).OrderBy(_ => Random.Range(0f, 1f));
		_colorsMap = new[] { 0, 1 }.Concat(fakeExtraColors).Concat(new[] { 5 }).ToArray();
		int[] fakeFractal = CreateFractal(false);
		for (int i = 0; i < 32; i++) _cells[i].Color = CellComponent.COLORS[_colorsMap[fakeFractal[i]]];
	}

	public override void OnActivate() {
		base.OnActivate();
		_decoder = _colorsMap;
		int[] extraColors;
		if (BombInfo.GetSerialNumberNumbers().Sum() % 6 == 0) extraColors = new[] { 2, 3, 4 };
		else if (BombInfo.GetPortPlateCount() > 2) extraColors = new[] { 4, 2, 3 };
		else if (BombInfo.GetIndicators().Count() > 2) extraColors = new[] { 3, 4, 2 };
		else if (BombInfo.GetBatteryCount() >= 5 || BombInfo.GetModuleNames().Count() > 63) extraColors = new[] { 2, 4, 3 };
		else if (Mathf.FloorToInt(BombInfo.GetTime()) / 60 >= 35) extraColors = new[] { 3, 2, 4 };
		else extraColors = new[] { 4, 3, 2 };
		_colorsMap = new[] { 0, 1 }.Concat(extraColors).Concat(new[] { 5 }).ToArray();
		Log("Colors: {0}", Enumerable.Range(0, 6).Select(i => COLORS_NAME[_colorsMap[i]]));
		_fractal = CreateFractal();
		_requiredSwapsCount = GetMinSwapsCount(_fractal, 0, 32);
		_ignoredModules = new HashSet<string>(BossModule.GetIgnoredModules("Forget Fractal", ForgetFractalData.DefaultIgnoredModules));
		List<string> allModules = BombInfo.GetSolvableModuleNames();
		int unignoredModulesCount = allModules.Where(m => !_ignoredModules.Contains(m)).Count();
		_stagesCount = unignoredModulesCount + 1;
		_cellsPerStage = 32 / _stagesCount;
		if (32 % _stagesCount > 0) _cellsPerStage += 1;
		if (_cellsPerStage < 3) _cellsPerStage = 3;
		GenerateStages();
		Log("Stages:");
		for (int i = 0; i < _stagesCount; i++) {
			char[] map = Enumerable.Range(0, 32).Select(_ => '.').ToArray();
			for (int j = 0; j < _cellsPerStage; j++) map[_stagesCells[i][j]] = COLORS_NAME[_colorsMap[_stagesColors[i][j]]];
			Log("{0}: <{1}>", i + 1, map.Join(""));
		}
		Log("Stages sum: <{0}>", _fractal.Select(stageValue => COLORS_NAME[_colorsMap[stageValue]]).Join(""));
		Log("Required swaps count: {0}", _requiredSwapsCount);
		RenderStage(0);
		Screen.OnInteract += () => { PressDisplay(); return false; };
		for (int i = 0; i < 32; i++) {
			int iC = i;
			_cells[iC].Selectable.OnInteract += () => { PressCell(iC); return false; };
		}
		_state = State.READ;
	}

	protected override void Update() {
		base.Update();
		if (_state != State.READ) return;
		int stageIndex = BombInfo.GetSolvedModuleNames().Where(s => !_ignoredModules.Contains(s)).Count();
		RenderStage(stageIndex);
	}

	public void PressDisplay() {
		if (_state == State.READY || _state == State.RECOVERY) {
			for (int i = 0; i < 32; i++) _cells[i].Color = BLACK;
			StageText.text = " 0/??";
			StageText.color = Color.cyan;
			_submition = Enumerable.Range(0, 32).Select(_ => -1).ToArray();
			_submitionsCount = 0;
			_state = State.SUBMIT;
			if (_animation != null) StopCoroutine(_animation);
			Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
		} else if (_state == State.SUBMIT) {
			if (_submitionsCount == 0) return;
			Log("Changes: <{0}>", _submition.Select(s => s < 0 ? '.' : COLORS_NAME[_colorsMap[_decoder[s]]]).Join(""));
			if (Enumerable.Range(0, 32).Any(i => _submition[i] >= 0 && _decoder[_submition[i]] == _fractal[i])) {
				Log("The color of the cell has been changed to the original. STRIKE!");
				Strike();
				StageText.color = Color.blue;
				for (int i = 0; i < 32; i++) {
					if (_submition[i] < 0 || _decoder[_submition[i]] == _fractal[i]) continue;
					_cells[i].Color = BLACK;
				}
				_state = State.RECOVERY;
				return;
			}
			int[] submitionFractal = Enumerable.Range(0, 32).Select(i => _submition[i] < 0 ? _fractal[i] : _decoder[_submition[i]]).ToArray();
			Log("Submission: <{0}>", submitionFractal.Select(cl => COLORS_NAME[_colorsMap[cl]]).Join(""));
			int strikesCount = GetMinSwapsCount(submitionFractal, 0, 32);
			if (strikesCount > 0) {
				int strikesLeft = Game.Mission.GeneratorSetting.NumStrikes - BombInfo.GetStrikes();
				strikesCount = Mathf.Max(1, Mathf.Min(strikesCount, strikesLeft - 1));
				Log("Fractal is invalid. STRIKE x{0}!", strikesCount);
				for (int i = 0; i < strikesCount; i++) Strike();
				StageText.color = Color.red;
				StartFractalRendering(submitionFractal);
				_state = State.RECOVERY;
			} else if (_submitionsCount > _requiredSwapsCount) {
				Log("Used {0} swaps. While {1} is possible. STRIKE!", _submitionsCount, _requiredSwapsCount);
				Strike();
				StageText.color = Color.magenta;
				StartFractalRendering(_fractal);
				_state = State.RECOVERY;
				_displaySwapsCount = true;
				UpdateSubmitionsCountDisplay();
			} else {
				Log("Module solved");
				Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
				Solve();
				StageText.color = Color.green;
				StageText.text = "SOLVED";
				StartFractalRendering(submitionFractal);
				_state = State.SOLVED;
			}
		}
	}

	public void PressCell(int cellIndex) {
		if (_state != State.SUBMIT) return;
		int cl = _submition[cellIndex] + 1;
		if (cl >= 6) {
			cl = -1;
			_cells[cellIndex].Color = BLACK;
			_submitionsCount -= 1;
		} else {
			_cells[cellIndex].Color = CellComponent.COLORS[_colorsMap[_decoder[cl]]];
			if (cl == 0) _submitionsCount += 1;
		}
		_submition[cellIndex] = cl;
		UpdateSubmitionsCountDisplay();
		Audio.PlaySoundAtTransform("press_0" + Random.Range(1, 4), transform);
	}

	private void UpdateSubmitionsCountDisplay() {
		string right = _displaySwapsCount ? _requiredSwapsCount.ToString().PadRight(2, ' ') : "??";
		StageText.text = string.Format("{0}/{1}", _submitionsCount.ToString().PadLeft(2, ' '), right);
	}

	private void RenderStage(int stageIndex) {
		if (stageIndex == _displayedStageIndex) return;
		int[] fractal = Enumerable.Range(0, 32).Select(_ => -1).ToArray();
		for (int i = 0; i < _cellsPerStage; i++) fractal[_stagesCells[stageIndex][i]] = _stagesColors[stageIndex][i];
		StartFractalRendering(fractal);
		_displayedStageIndex = stageIndex;
		int maxLength = _stagesCount.ToString().Length;
		StageText.text = string.Format("{0}/{1}", (stageIndex + 1).ToString().PadLeft(maxLength, ' '), _stagesCount);
		if (stageIndex + 1 == _stagesCount) {
			StageText.color = Color.green;
			_state = State.READY;
		}
	}

	private void StartFractalRendering(int[] fractal) {
		if (_animation != null) StopCoroutine(_animation);
		_animation = StartCoroutine(RenderFractal(fractal));
	}

	private IEnumerator RenderFractal(int[] fractal) {
		float startTime = Time.time;
		int renderedStagesCount = 0;
		int[] stagesQueue = Enumerable.Range(0, 32).OrderBy(_ => Random.Range(0f, 1f)).ToArray();
		while (true) {
			yield return null;
			float passedTime = Time.time - startTime;
			int stagesToRender = Mathf.Min(32, Mathf.FloorToInt(32 * passedTime / ANIMATION_DURATION));
			for (int i = renderedStagesCount; i < stagesToRender; i++) {
				int cellIndex = stagesQueue[i];
				_cells[cellIndex].Color = fractal[cellIndex] < 0 ? BLACK : CellComponent.COLORS[_colorsMap[fractal[cellIndex]]];
			}
			renderedStagesCount = stagesToRender;
			if (renderedStagesCount >= 32) yield break;
		}
	}

	private void GenerateStages() {
		List<int>[] stages = Enumerable.Range(0, _stagesCount).Select(_ => new List<int>()).ToArray();
		for (int i = 0; i < 32; i++) {
			while (true) {
				int stageIndex = Random.Range(0, _stagesCount);
				if (stages[stageIndex].Count >= _cellsPerStage) continue;
				stages[stageIndex].Add(i);
				break;
			}
		}
		for (int i = 0; i < _stagesCount; i++) {
			while (stages[i].Count < _cellsPerStage) {
				int cellIndex = Random.Range(0, 32);
				if (stages[i].Contains(cellIndex)) continue;
				stages[i].Add(cellIndex);
			}
		}
		_stagesCells = new int[_stagesCount][];
		_stagesColors = new int[_stagesCount][];
		bool[] cellDefined = new bool[32];
		for (int i = _stagesCount - 1; i >= 0; i--) {
			_stagesCells[i] = stages[i].ToArray();
			_stagesColors[i] = new int[_cellsPerStage];
			for (int j = 0; j < _cellsPerStage; j++) {
				int cellIndex = _stagesCells[i][j];
				if (cellDefined[cellIndex]) {
					_stagesColors[i][j] = Random.Range(0, 6);
					continue;
				}
				_stagesColors[i][j] = _fractal[cellIndex];
				cellDefined[cellIndex] = true;
			}
		}
	}

	private static int[] CreateFractal(bool withErrors = true) {
		int[] result = new int[32];
		BuildFractal(result, 0, 32);
		if (withErrors) {
			for (int i = 0; i < 32; i++) result[Random.Range(0, 32)] = Random.Range(0, 6);
			if (GetMinSwapsCount(result, 0, 32) == 0) {
				int ind = Random.Range(0, 32);
				result[ind] = (result[ind] + Random.Range(1, 6)) % 6;
			}
		}
		return result;
	}

	private static void BuildFractal(int[] arr, int from, int to, int depth = 0) {
		if (from + 1 >= to) {
			arr[from] = depth;
			return;
		}
		int pivot = (from + to) / 2;
		if (Random.Range(0, 2) == 0) {
			Fill(arr, from, pivot, depth);
			BuildFractal(arr, pivot, to, depth + 1);
		} else {
			BuildFractal(arr, from, pivot, depth + 1);
			Fill(arr, pivot, to, depth);
		}
	}

	private static void Fill(int[] arr, int from, int to, int value) {
		for (int i = from; i < to; i++) arr[i] = value;
	}

	private static int GetMinSwapsCount(int[] fractal, int from, int to, int depth = 0) {
		if (from + 1 >= to) {
			return fractal[from] == depth ? 0 : 1;
		}
		int pivot = (from + to) / 2;
		int a = Enumerable.Range(from, pivot - from).Count(i => fractal[i] != depth) + GetMinSwapsCount(fractal, pivot, to, depth + 1);
		int b = Enumerable.Range(pivot, to - pivot).Count(i => fractal[i] != depth) + GetMinSwapsCount(fractal, from, pivot, depth + 1);
		return Mathf.Min(a, b);
	}
}
