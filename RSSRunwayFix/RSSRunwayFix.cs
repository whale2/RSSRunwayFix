using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using System.Linq;

namespace RSSRunwayFix
{
	[KSPAddon(KSPAddon.Startup.Flight, false)]

	public class RSSRunwayFix: MonoBehaviour
	{
		private bool hold = false;

		[KSPField(isPersistant = true)] 
		public bool debug = true;
		
		[KSPField(isPersistant = true)]
		public float holdThreshold = 2700;
		
		public float holdThresholdSqr;

		public float originalThreshold = 0;
		public float originalThresholdSqr = 0;

		private int layerMask = 1<<15;

		private int frameSkip = 0;
		private bool rwy = false;

		private Vector3 down;

		private string[] collidersToFix =
		{
			"Section4", "Section3", "Section2", "Section1", "End27", "End09",
		};
		
		public void Start() {
			
			printDebug("Start");
			
			GameEvents.onVesselGoOffRails.Add (onVesselGoOffRails);
			GameEvents.onVesselGoOnRails.Add (onVesselGoOnRails);
			GameEvents.onVesselSwitching.Add (onVesselSwitching);
			GameEvents.onVesselSituationChange.Add (onVesselSituationChange);
			
			//
			GameEvents.onFloatingOriginShift.Add(onFloatingOriginShift);
		}

		public void OnDestroy() {
			printDebug("OnDestroy");
			GameEvents.onVesselGoOffRails.Remove (onVesselGoOffRails);
			GameEvents.onVesselGoOnRails.Remove (onVesselGoOnRails);
			GameEvents.onVesselSwitching.Remove (onVesselSwitching);
			GameEvents.onFloatingOriginShift.Remove(onFloatingOriginShift);
			GameEvents.onVesselSituationChange.Remove(onVesselSituationChange);
		}

		public void onFloatingOriginShift(Vector3d v0, Vector3d v1)
		{
			if (!hold)
			{
				return;
			}
			printDebug($"RSSRWF: v0: {v0}, v1: {v1}, threshold: {FloatingOrigin.fetch.threshold}");
		}

		public void onVesselGoOnRails(Vessel v)
		{
			FloatingOrigin.fetch.threshold = originalThreshold;
			FloatingOrigin.fetch.thresholdSqr = originalThresholdSqr;
			hold = false;
		}
			
		public void onVesselGoOffRails(Vessel v) {

			printDebug("started");

			originalThreshold = FloatingOrigin.fetch.threshold;
			originalThresholdSqr = FloatingOrigin.fetch.thresholdSqr;

			printDebug($"original threshold={originalThreshold}");
			holdThresholdSqr = holdThreshold * holdThreshold;

			GameObject end09 = GameObject.Find(collidersToFix[0]);
			if (end09 == null)
			{
				hold = false;
				return;
			}

			combine(end09.transform.parent.gameObject);
			getDownwardVector();
			hold = true;
		}

		public void onVesselSwitching(Vessel from, Vessel to) {

			if (to == null || to.situation != Vessel.Situations.LANDED) { // FIXME: Do we need PRELAUNCH here?
				return;
			}

			getDownwardVector();
		}

		private void getDownwardVector()
		{
			Vessel v = FlightGlobals.ActiveVessel;
			down = (v.CoM - v.mainBody.transform.position).normalized * -1;
		}

		public void onVesselSituationChange(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> data)
		{
			hold = data.to == Vessel.Situations.LANDED && data.host == FlightGlobals.ActiveVessel;
			
			printDebug($"vessel: {data.host.vesselName}, situation: {data.to}, hold: {hold}");
			StartCoroutine(restoreThreshold());
		}

		private IEnumerator restoreThreshold()
		{
			while (!hold && FlightGlobals.ActiveVessel.radarAltitude < 3f)
			{
				yield return new WaitForSeconds(1);
			}
			
			if (!hold && FloatingOrigin.fetch.threshold > originalThreshold && originalThreshold > 0)
			{
				printDebug($"Restoring original thresholds ({FloatingOrigin.fetch.threshold} > {originalThreshold})");
				FloatingOrigin.fetch.threshold = originalThreshold;
				FloatingOrigin.fetch.thresholdSqr = originalThresholdSqr;
			}
		}
		
		public void FixedUpdate()
		{
			frameSkip++;
			if (frameSkip < 10)
			{
				return;
			}
			frameSkip = 0;
			
			if (!checkRunway())
			{
				if (rwy)
				{
					printDebug(
						$"rwy=false; threshold={FloatingOrigin.fetch.threshold}, original threshold={originalThreshold}");
					rwy = false;
				}
				
				return;
			}
			
			FloatingOrigin.fetch.threshold = holdThreshold;
			FloatingOrigin.fetch.thresholdSqr = holdThresholdSqr;
			
			if (!rwy)
			{
				printDebug($"rwy=true; threshold={FloatingOrigin.fetch.threshold}, original threshold={originalThreshold}");
				rwy = true;
			}

			FloatingOrigin.SetSafeToEngage(false);
		}
		
		private bool checkRunway()
		{
			if (!hold)
			{
				return false;
			}
			
			Vessel v = FlightGlobals.ActiveVessel;
			if (v.situation != Vessel.Situations.LANDED && v.situation != Vessel.Situations.PRELAUNCH)
			{
				return false;
			}
			
			getDownwardVector();
			
			RaycastHit raycastHit;
			bool hit = Physics.Raycast(v.transform.position, down, out raycastHit, 100, layerMask);
			if (!hit)
			{
				return false;
			}
			
			string colliderName = raycastHit.collider.gameObject.name;
			if (colliderName != "runway_collider")
			{
				return false;
			}

			return true;
		}

		internal void printDebug(String message) {

			if (!debug)
				return;
			StackTrace trace = new StackTrace ();
			String caller = trace.GetFrame(1).GetMethod ().Name;
			int line = trace.GetFrame (1).GetFileLineNumber ();
			print ("RSSSteamRoller: " + caller + ":" + line + ": " + message);
		}


		private void combine(GameObject parent)
		{
			
			MeshFilter[] meshFilters = new MeshFilter[collidersToFix.Length];

			int i = 0;
			foreach (string c in collidersToFix)
			{
				GameObject o = GameObject.Find(c);
				if (!o.activeInHierarchy)
				{
					continue;
				}
				meshFilters[i] = o.GetComponentInChildren<MeshFilter>();
			}
			CombineInstance[] combine = new CombineInstance[meshFilters.Length];

			 
			i = 0;
			while (i < meshFilters.Length)
			{
				combine[i].mesh = meshFilters[i].sharedMesh;
				combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
				meshFilters[i].gameObject.SetActive(false);

				i++;
			}
			
			parent.transform.GetComponent<MeshFilter>().mesh = new Mesh();
			parent.transform.GetComponent<MeshFilter>().mesh.CombineMeshes(combine);
			parent.transform.gameObject.SetActive(true);
			parent.GetComponent<Renderer>().material.CopyPropertiesFromMaterial(
			parent.GetComponentInChildren<Renderer>().material);
		}

	}
}
