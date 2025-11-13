using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;

public class GameManager : MonoBehaviour
{
    private static GameManager _instance;

    public static GameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<GameManager>();
            }

            return _instance;
        }
    }

    [Header("World Data")] public string CurrentWorldName = string.Empty;
    public bool PendingExitToMenu = false;

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            Application.targetFrameRate = 30;
            QualitySettings.vSyncCount = 0;
            DontDestroyOnLoad(this.gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    [Header("Build Mode")] public bool BuildModeActive = false;
    [SerializeField] private PlayerMovement _playerMovement; // кеш ссылки на движение игрока

    public void RegisterPlayerMovement(PlayerMovement pm)
    {
        if (pm != null)
            _playerMovement = pm;
    }

    public void ToggleBuildMode()
    {
        SetBuildMode(!BuildModeActive);
    }

    public void SetBuildMode(bool active)
    {
        BuildModeActive = active;
        if (_playerMovement != null)
            _playerMovement.SetLevitateMode(active);
    }

    public void LevitateUp(bool isPressed)
    {
        if (_playerMovement != null)
            _playerMovement.SetLevitateUp(isPressed);
    }

    public void LevitateDown(bool isPressed)
    {
        if (_playerMovement != null)
            _playerMovement.SetLevitateDown(isPressed);
    }
}
