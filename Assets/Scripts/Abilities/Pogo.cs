using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pogo : MonoBehaviour, IAbility
{
    float abilityTime = 1;
    float jumpHeight = 5f;

    public IEnumerator ExecuteAbility(Vector3 abilitySquare)
    {
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = abilitySquare + new Vector3(0, GetComponent<Collider>().bounds.size.y / 2, 0);

        transform.GetComponent<Movement>().StopMovement();
        transform.GetComponent<Shooting>().StopShooting();
        transform.GetComponent<Movement>().moving = true;
        transform.GetComponent<Collider>().enabled = false;


        float elapsed = 0f;

        while (elapsed < abilityTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / abilityTime;
            
            // Interpolate between start and target positions
            Vector3 currentPos = Vector3.Lerp(startPosition, targetPosition, progress);
            currentPos.y += jumpHeight * 4 * progress * (1 - progress);

            transform.position = currentPos;
            
            yield return null; // Wait for next frame
        }
        transform.position = targetPosition;

        transform.GetComponent<Collider>().enabled = true;
        transform.GetComponent<Movement>().transitionToShooting();
    }
}
