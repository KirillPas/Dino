using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using static UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics.HapticsUtility;

public class GrabParenter : MonoBehaviour
{
    public void OnGrab(SelectEnterEventArgs args)
    {
        args.interactableObject.transform.SetParent(args.interactorObject.transform);
    }

    public void OnUnGrab(SelectExitEventArgs args)
    {
        if (args != null)
        {
            args.interactableObject.transform.SetParent(null);
        }
    }
}
