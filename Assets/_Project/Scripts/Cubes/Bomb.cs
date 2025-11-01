using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Bomb : MonoBehaviour
{
    [SerializeField] private float _explosionRadius = 1.5f;
    [SerializeField] private int _maxCubesPerFrame = 10; // Максимум кубов обрабатываемых за кадр
    [SerializeField] Camera _camera;


    private void Update()
    {
        if (InputManager.Instance._TOUCH) return;

        if (Input.GetKeyDown(KeyCode.T))
        {
            Raycast();
        }
    }

    public void Raycast()
    {
        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = _camera.ScreenPointToRay(screenCenterPoint);
        if (Physics.Raycast(ray, out RaycastHit hit, 200))
        {
            if (hit.collider.TryGetComponent(out Cube cube))
            {
                cube.Destroy();
            }

            StartCoroutine(ExplosionCoroutine(hit.point));
        }
    }

    // Оптимизированный взрыв: распределяем обработку кубов на несколько кадров
    // Это предотвращает создание сотен Rigidbody и применение сил одновременно
    private IEnumerator ExplosionCoroutine(Vector3 point)
    {
        var colliders = Physics.OverlapSphere(point, _explosionRadius);
        List<Cube> cubesToProcess = new List<Cube>();

        // Собираем все кубы для обработки
        foreach (Collider hitCollider in colliders)
        {
            if (hitCollider.TryGetComponent(out Cube cube))
            {
                if (!cube.Detouched)
                {
                    cubesToProcess.Add(cube);
                }
                else
                {
                    cube.Destroy();
                }
            }
        }

        // Обрабатываем кубы батчами по несколько кадров
        // Это снижает нагрузку на физику Unity и предотвращает WaitForJobGroupID
        int processed = 0;
        while (processed < cubesToProcess.Count)
        {
            int batchSize = Mathf.Min(_maxCubesPerFrame, cubesToProcess.Count - processed);

            // Обрабатываем батч кубов
            for (int i = 0; i < batchSize; i++)
            {
                Cube cube = cubesToProcess[processed + i];
                if (cube == null || cube.Detouched) continue;

                cube.Detouch();

                // Получаем или добавляем Rigidbody
                Rigidbody rb = cube.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = cube.gameObject.AddComponent<Rigidbody>();
                    rb.mass = 1f;
                    rb.drag = 0.5f;
                    rb.angularDrag = 0.5f;
                }

                // Применяем силу взрыва
                rb.AddExplosionForce(1000f, point, _explosionRadius);
            }

            processed += batchSize;

            // Ждем следующий FixedUpdate перед обработкой следующего батча
            // Это дает Unity Physics время обработать текущие изменения
            yield return new WaitForFixedUpdate();
        }
    }
}
