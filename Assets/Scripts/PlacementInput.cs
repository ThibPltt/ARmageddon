using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// Outils d'entrée partagés par les scripts de placement.
public static class PlacementInput
{
    private static readonly List<RaycastResult> hits = new List<RaycastResult>();
    private static PointerEventData pointerData;
    private static EventSystem pointerDataOwner;

    // Vrai si le doigt (ou la souris dans l'éditeur) est sur un élément de l'interface.
    // On interroge directement les Raycasters de l'interface à la position du doigt :
    // IsPointerOverGameObject() répond souvent « non » à la frame où le doigt se pose,
    // ce qui faisait passer l'appui sur un bouton jusqu'au terrain derrière lui.
    public static bool IsPointerOverUI()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;

        if (Input.touchCount > 0)
        {
            for (int t = 0; t < Input.touchCount; t++)
                if (IsOverUI(es, Input.GetTouch(t).position)) return true;
            return false;
        }
        return IsOverUI(es, Input.mousePosition);
    }

    private static bool IsOverUI(EventSystem es, Vector2 screenPosition)
    {
        if (pointerData == null || pointerDataOwner != es)
        {
            pointerData = new PointerEventData(es);
            pointerDataOwner = es;
        }
        pointerData.position = screenPosition;
        hits.Clear();
        es.RaycastAll(pointerData, hits);
        for (int i = 0; i < hits.Count; i++)
        {
            // Seuls les éléments qui bloquent les rayons comptent (images, boutons, textes cliquables).
            if (hits[i].gameObject != null) return true;
        }
        return false;
    }
}
