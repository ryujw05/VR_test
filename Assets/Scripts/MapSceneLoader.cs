using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MapSceneLoader : MonoBehaviour
{
    [Header("Settings")]
    public string mapSceneName = "Map";
    [SerializeField] private FloorRoomStateMachine floorSM;
    public string mapRootName = "MapRoot";

    private Transform currentRoomAnchor;

    private void Awake()
    {
        if (floorSM == null)
            floorSM = FindObjectOfType<FloorRoomStateMachine>();

        if (floorSM != null)
        {
            floorSM.onRoomFixed.AddListener(OnRoomFixed);
        }
    }

    public void OnRoomFixed(Transform roomAnchor)
    {
        currentRoomAnchor = roomAnchor;
        StartCoroutine(LoadMapSceneAdditive());
    }

    private IEnumerator LoadMapSceneAdditive()
    {
        if (SceneManager.GetSceneByName(mapSceneName).isLoaded)
            yield break;

        var op = SceneManager.LoadSceneAsync(mapSceneName, LoadSceneMode.Additive);
        if (op == null) yield break;

        while (!op.isDone) yield return null;

        var mapScene = SceneManager.GetSceneByName(mapSceneName);
        var roots = mapScene.GetRootGameObjects();
        GameObject mapRoot = null;
        foreach (var go in roots)
        {
            if (go.name == mapRootName)
            {
                mapRoot = go;
                break;
            }
        }

        if (mapRoot == null || currentRoomAnchor == null) yield break;

        // mapRoot.transform.SetParent(currentRoomAnchor, false);
        mapRoot.transform.SetParent(null); 
        mapRoot.transform.position = currentRoomAnchor.position;
        mapRoot.transform.rotation = currentRoomAnchor.rotation;
        mapRoot.transform.localPosition = Vector3.zero;
        mapRoot.transform.localRotation = Quaternion.identity;
        mapRoot.transform.localScale = Vector3.one;
    }
}
