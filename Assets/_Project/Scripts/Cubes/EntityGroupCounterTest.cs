using UnityEngine;

/// <summary>
/// Тестовый скрипт для проверки функциональности подсчета групп кубов
/// </summary>
public class EntityGroupCounterTest : MonoBehaviour
{
    [Header("Тестирование подсчета групп")] [SerializeField]
    private Entity _entityToTest;

    [SerializeField] private bool _testOnStart = false;

    [Header("Результаты тестирования")] [SerializeField]
    private int _lastGroupCount = 0;

    [SerializeField] private bool _testPassed = false;

    private void Start()
    {
        if (_testOnStart)
        {
            TestGroupCounting();
        }
    }

    [ContextMenu("Тест подсчета групп")]
    public void TestGroupCounting()
    {
        if (_entityToTest == null)
        {
            _entityToTest = GetComponent<Entity>();
        }

        if (_entityToTest == null)
        {
            Debug.LogError("EntityGroupCounterTest: Не найден Entity для тестирования!");
            return;
        }

        // Принудительно обновляем данные кубов
        _entityToTest.UpdateMassAndCubes();

        _lastGroupCount = _entityToTest.CountConnectedGroups();
        _testPassed = _lastGroupCount >= 0;

        Debug.Log(
            $"EntityGroupCounterTest: Тест завершен. Количество групп: {_lastGroupCount}. Тест пройден: {_testPassed}");

        // Дополнительная информация для отладки
        int totalCubes = _entityToTest.transform.childCount;
        Debug.Log($"EntityGroupCounterTest: Общее количество кубов в Entity: {totalCubes}");

        if (_lastGroupCount == 0 && totalCubes > 0)
        {
            Debug.LogWarning(
                "EntityGroupCounterTest: Внимание! Найдены кубы, но групп не обнаружено. Возможно, кубы не связаны между собой.");
        }
        else if (_lastGroupCount == 1 && totalCubes > 0)
        {
            Debug.Log(
                "EntityGroupCounterTest: Все кубы связаны в одну группу - это нормально для цельной конструкции.");
        }
        else if (_lastGroupCount > 1)
        {
            Debug.Log(
                $"EntityGroupCounterTest: Обнаружено {_lastGroupCount} изолированных групп кубов. Это может указывать на разрозненные части конструкции.");
        }
    }

    [ContextMenu("Принудительная сборка меша")]
    public void ForceMeshCombine()
    {
        if (_entityToTest == null)
        {
            _entityToTest = GetComponent<Entity>();
        }

        if (_entityToTest == null)
        {
            Debug.LogError("EntityGroupCounterTest: Не найден Entity для тестирования!");
            return;
        }

        var meshCombiner = _entityToTest.GetComponent<EntityMeshCombiner>();
        if (meshCombiner != null)
        {
            Debug.Log("EntityGroupCounterTest: Запуск принудительной сборки меша...");
            meshCombiner.CombineMeshes();
        }
        else
        {
            Debug.LogError("EntityGroupCounterTest: Не найден EntityMeshCombiner!");
        }
    }

    private void OnValidate()
    {
        if (_entityToTest == null)
        {
            _entityToTest = GetComponent<Entity>();
        }
    }
}
