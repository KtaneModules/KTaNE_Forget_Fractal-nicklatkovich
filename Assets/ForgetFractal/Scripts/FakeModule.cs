using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FakeModule : KtaneModule {
	public KMSelectable SolveButton;

	public override void OnActivate() {
		base.OnActivate();
		SolveButton.OnInteract += () => { Solve(); return false; };
	}
}
