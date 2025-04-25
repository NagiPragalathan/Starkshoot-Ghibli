using UnityEngine;
using Photon.Pun;

public class Bullet : MonoBehaviour
{
    public int damage = 20;
    public float speed = 30f;
    public float maxDistance = 100f;
    
    private Vector3 startPosition;
    private Rigidbody rb;

    private void Start()
    {
        startPosition = transform.position;
        rb = GetComponent<Rigidbody>();
        
        // Ensure the bullet stays horizontal
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        }

        // Set initial position slightly above ground to prevent collision with ground
        transform.position = new Vector3(transform.position.x, 1f, transform.position.z);
    }

    private void Update()
    {
        // Maintain horizontal movement
        if (rb != null)
        {
            Vector3 velocity = rb.velocity;
            velocity.y = 0; // Keep vertical velocity at 0
            rb.velocity = velocity;
        }

        // Check if bullet has traveled too far
        if (Vector3.Distance(startPosition, transform.position) > maxDistance)
        {
            Destroy(gameObject);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Check if we hit a player
        PlayerHealth playerHealth = collision.gameObject.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.TakeDamage(damage, "NPC");
        }

        // Create hit effect
        CreateHitEffect(collision);

        // Destroy the bullet
        Destroy(gameObject);
    }

    private void CreateHitEffect(Collision collision)
    {
        ContactPoint contact = collision.contacts[0];
        
        // Create hit effect that stays horizontal
        GameObject hitEffect = new GameObject("HitEffect");
        hitEffect.transform.position = contact.point;
        
        // Keep the effect horizontal
        Vector3 effectDirection = contact.normal;
        effectDirection.y = 0;
        hitEffect.transform.rotation = Quaternion.LookRotation(effectDirection);
        
        ParticleSystem particles = hitEffect.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.startSize = 0.2f;
        main.startSpeed = 2f;
        main.startLifetime = 0.2f;
        main.maxParticles = 20;
        
        // Configure particle system to emit horizontally
        var shape = particles.shape;
        shape.angle = 0;
        // shape.rotation = new Vector3(0, 0, 0);
        
        Destroy(hitEffect, 1f);
    }
}
