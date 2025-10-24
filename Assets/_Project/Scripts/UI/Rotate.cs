using UnityEngine;

public class Rotate : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 90f; // Скорость вращения в градусах в секунду
    [SerializeField] private bool clockwise = true; // Направление вращения: true - по часовой, false - против часовой

    void Update()
    {
        // Определяем направление вращения
        float direction = clockwise ? 1f : -1f;

        // Вращаем объект вокруг оси Z с заданной скоростью и направлением
        transform.Rotate(0, 0, rotationSpeed * direction * Time.deltaTime);
    }
}
