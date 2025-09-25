using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class DinoPatrol : MonoBehaviour
{
    public Transform[] patrolPoints;
    public float pointReachDistance = 1f;
    public float waitTime = 12f;
    
    private NavMeshAgent agent;
    private Animator animator;
    private int currentPointIndex = 0;
    private bool isWaiting = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        if (patrolPoints.Length > 0)
        {
            agent.SetDestination(patrolPoints[currentPointIndex].position);
        }
    }

    void Update()
    {
        if (!isWaiting)
        {
            if (agent.remainingDistance < pointReachDistance && !agent.pathPending)
            {
                StartCoroutine(WaitAtPoint());
            }
        }
    }

    private IEnumerator WaitAtPoint()
    {
        isWaiting = true;
        agent.isStopped = true;
        animator.SetBool("Speed", true);
        yield return new WaitForSeconds(2);
        animator.SetBool("Eat", true);
        yield return new WaitForSeconds(waitTime);
        currentPointIndex = (currentPointIndex + 1) % patrolPoints.Length;
        agent.SetDestination(patrolPoints[currentPointIndex].position);
        yield return new WaitForSeconds(2);
        agent.isStopped = false;
        animator.SetBool("Eat", false);
        animator.SetBool("Speed", false);
        isWaiting = false;
    }
}
