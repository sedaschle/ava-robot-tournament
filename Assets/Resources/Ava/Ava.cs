using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

/*

Ben Vinnick, Sofia Daschle

Summary:
We modeled our agent via a reward system that reinforces doing certain actions. 

The main strategies implemented are:
1. When we don't have the majority of balls, reward going out and collecting them.
2. While collecting balls, only carry two balls at a time so that our speed is not reduced. 
3. Once we have the majority of the balls in the game, focus on "guarding" our base by not picking up more balls,
   and instead staying near the base and shooting the laser.

Overall our strategy is to quickly and efficiently get the majority of the balls in our home base, at which point
we minimize risks by defending our home base until the time is up. We used behavioural training and GAIL to accomplish this.

*/
public class Ava : CogsAgent
{
	// current balls agent is carrying
	int balls = 0;
	// current balls in agents home base
	int ballsInBase = 0;
	// ------------------BASIC MONOBEHAVIOR FUNCTIONS-------------------

	// Initialize values
	protected override void Start()
	{
		base.Start();
		AssignBasicRewards();
	}

	// For actual actions in the environment (e.g. movement, shoot laser)
	// that is done continuously
	protected override void FixedUpdate()
	{
		base.FixedUpdate();

		LaserControl();
		// Movement based on DirToGo and RotateDir
		moveAgent(dirToGo, rotateDir);

		// dropped balls -> reset carried balls to 0, negative reward
		if (IsFrozen())
		{
			AddReward(-0.3f);
			balls = 0;
		}

		int countBase = 0;

		// count the number of balls in our home base 
		foreach (GameObject target in targets)
		{
			if (target.GetComponent<Target>().GetInBase() == GetTeam()) 
			{
				countBase++;
			}
		}
		// update count of balls in base
		ballsInBase = countBase;
	}


	// --------------------AGENT FUNCTIONS-------------------------

	// Get relevant information from the environment to effectively learn behavior
	public override void CollectObservations(VectorSensor sensor)
	{
		// Agent velocity in x and z axis 
		var localVelocity = transform.InverseTransformDirection(rBody.velocity);
		sensor.AddObservation(localVelocity.x);
		sensor.AddObservation(localVelocity.z);

		// Time remaning
		sensor.AddObservation(timer.GetComponent<Timer>().GetTimeRemaning());

		// Agent's current rotation
		var localRotation = transform.rotation;
		sensor.AddObservation(transform.rotation.y);

		// Agent and home base's position
		sensor.AddObservation(this.transform.localPosition);
		sensor.AddObservation(baseLocation.localPosition);

		// for each target in the environment, add: its position, whether it is being carried,
		// and whether it is in a base
		foreach (GameObject target in targets)
		{
			sensor.AddObservation(target.transform.localPosition);
			sensor.AddObservation(target.GetComponent<Target>().GetCarried());
			sensor.AddObservation(target.GetComponent<Target>().GetInBase()

	    );
		}

		// Whether the agent is frozen
		sensor.AddObservation(IsFrozen());
	}

	// For manual override of controls. This function will use keyboard presses to simulate output from your NN 
	public override void Heuristic(float[] actionsOut)
	{
		var discreteActionsOut = actionsOut;
		discreteActionsOut[0] = 0; //Simulated NN output 0 MOVE UP OR DOWN
		discreteActionsOut[1] = 0; //....................1 MOVE RIGHT OR LEFT
		discreteActionsOut[2] = 0; //....................2 SHOOT
		discreteActionsOut[3] = 0; //....................3 GO TO NEAREST TARGET
		discreteActionsOut[4] = 0; //....................4 GO BACK TO BASE


		if (Input.GetKey(KeyCode.UpArrow))
		{
			discreteActionsOut[0] = 1;
		}
		if (Input.GetKey(KeyCode.DownArrow))
		{
			discreteActionsOut[0] = 2;
		}
		if (Input.GetKey(KeyCode.RightArrow))
		{
			discreteActionsOut[1] = 1;
		}
		if (Input.GetKey(KeyCode.LeftArrow))
		{
			discreteActionsOut[1] = 2;
		}

		//Shoot
		if (Input.GetKey(KeyCode.Space))
		{
			discreteActionsOut[2] = 1;
		}

		//GoToNearestTarget
		if (Input.GetKey(KeyCode.A))
		{
			discreteActionsOut[3] = 1;
		}

		if (Input.GetKey(KeyCode.S))
		{
			discreteActionsOut[4] = 1;
		}
	}

	// What to do when an action is received (i.e. when the Brain gives the agent information about possible actions)
	public override void OnActionReceived(float[] act)
	{
		int forwardAxis = (int)act[0]; 
		int rotateAxis = (int)act[1];
		int shootAxis = (int)act[2];
		int goToTargetAxis = (int)act[3];
		int goToBaseAxis = (int)act[4];

		MovePlayer(forwardAxis, rotateAxis, shootAxis, goToTargetAxis, goToBaseAxis);
	}


	// ----------------------ONTRIGGER AND ONCOLLISION FUNCTIONS------------------------
	// Called when object collides with or trigger (similar to collide but without physics) other objects
	protected override void OnTriggerEnter(Collider collision)
	{

		if (collision.gameObject.CompareTag("HomeBase") && collision.gameObject.GetComponent<HomeBase>().team == GetTeam())
		{
			// agent has returned to base, assign rewards based on case (according to strategy)
			base.OnTriggerEnter(collision);
			
			if (ballsInBase > 4) // have majority of balls in our home base, and:
			{
				if (balls == 0) // agent is returning without more balls  to "defend" - positive reward
				{
					SetReward(1.0f); 
					EndEpisode();
				}
				else // agent is returning with more balls instead of having stayed to "defend" - negative reward
				{
					ballsInBase += balls;
					balls = 0;
					SetReward(-1.0f);
					EndEpisode();
				}
			}
			else // don't have majority of balls in our home base, and:
			{
				if (balls == 2 || (balls == 1 && ballsInBase == 4)) // agent is returning with maximum amount of balls - positive reward
				{
					ballsInBase += balls;
					balls = 0;
					SetReward(1.0f);
					EndEpisode();
				}
				else // agent is returning without balls, or with more than two balls  - negative reward
				{
					balls = 0;
					SetReward(-1.0f);
					EndEpisode();
				}
			}
		}
	}

	protected override void OnCollisionEnter(Collision collision)
	{


		//target is not in my base and is not being carried and I am not frozen
		if (collision.gameObject.CompareTag("Target") && collision.gameObject.GetComponent<Target>().GetInBase() != GetTeam() && collision.gameObject.GetComponent<Target>().GetCarried() == 0 && !IsFrozen())
		{
			balls++;
			if (balls <= 2 && ballsInBase < 5) // picking up maximum two balls, when we do not have majority - positive reward
			{
				AddReward(0.5f);
			}
			else // picking up too many balls, or picking up balls when we already have majority - negative reward
			{
				AddReward(-0.5f);
			}
		}

		// negative reward for hitting a wall
		if (collision.gameObject.CompareTag("Wall"))
		{ 
			AddReward(-0.7f);
		}
		base.OnCollisionEnter(collision);
	}

	//  --------------------------HELPERS---------------------------- 
	private void AssignBasicRewards()
	{
		rewardDict = new Dictionary<string, float>();

		rewardDict.Add("frozen", 0f);
		rewardDict.Add("shooting-laser", 0f);
		rewardDict.Add("hit-enemy", 0f);
		rewardDict.Add("dropped-one-target", 0f);
		rewardDict.Add("dropped-targets", 0f);
	}

	private void MovePlayer(int forwardAxis, int rotateAxis, int shootAxis, int goToTargetAxis, int goToBaseAxis)
	{
		dirToGo = Vector3.zero;
		rotateDir = Vector3.zero;

		Vector3 forward = transform.forward;
		Vector3 backward = -transform.forward;
		Vector3 right = transform.up;
		Vector3 left = -transform.up;

		//fowardAxis: 
		// 0 -> do nothing
		// 1 -> go forward
		// 2 -> go backward
		if (forwardAxis == 0)
		{
			//do nothing. This case is not necessary to include, it's only here to explicitly show what happens in case 0
		}
		else if (forwardAxis == 1)
		{
			dirToGo = forward;
		}
		else if (forwardAxis == 2)
		{
			dirToGo = backward;
		}

		//rotateAxis: 
		// 0 -> do nothing
		// 1 -> go right
		// 2 -> go left
		if (rotateAxis == 0)
		{
			//do nothing
		}
		else if (rotateAxis == 1)
		{
			rotateDir = right;
		}
		else if (rotateAxis == 2)
		{
			rotateDir = left;
		}

		// shoot laser
		// We want agent to "guard" (shoot) when we have majority and find balls (not shoot) when we don't
		if (shootAxis == 1)
		{
			if (ballsInBase > 4) // add a positive reward for shooting when we do have the majority 
			{
				AddReward(0.5f);
			}
			else // add a negative reward for shooting when we don't have majority
			{
				AddReward(-0.5f);
			}
			SetLaser(true);
		}
		else
		{
			SetLaser(false);
		}

		// go to the nearest target
		if (goToTargetAxis == 1)
		{
			if (balls < 2 && ballsInBase < 4 || // move to ball when we don't have majority and are carrying < 2 balls - positive reward
			balls == 0 && ballsInBase == 4) 
			{
				AddReward(0.2f);
			}
			else // in this case, any other behaviour will be bad - negative reward
			{
				AddReward(-0.2f);
			}
			GoToNearestTarget();
		}

		// go to base
		if (goToBaseAxis == 1) 
		{
			// agent is returning with 2 balls, OR agent is only returning with 1 but already has 4 in base - positive reward
			if ((balls == 2 && ballsInBase < 5) || (balls == 1 && ballsInBase == 4)) 
			{
				AddReward(0.2f);
			}

			// agent is returning with more than 2 balls, only 1 when it doesn't yet have majority, etc. - negative reward
			else 
			{
				AddReward(-0.2f);
			}
			GoToBase();
		}
	}

	// Go to home base
	private void GoToBase()
	{
		TurnAndGo(GetYAngle(myBase));
	}

	// Go to the nearest target
	private void GoToNearestTarget()
	{
		GameObject target = GetNearestTarget();
		if (target != null)
		{
			float rotation = GetYAngle(target);
			TurnAndGo(rotation);
		}
	}

	// Rotate and go in specified direction
	private void TurnAndGo(float rotation)
	{

		if (rotation < -5f)
		{
			rotateDir = transform.up;
		}
		else if (rotation > 5f)
		{
			rotateDir = -transform.up;
		}
		else
		{
			dirToGo = transform.forward;
		}
	}

	// return reference to nearest target
	protected GameObject GetNearestTarget()
	{
		float distance = 200;
		GameObject nearestTarget = null;
		foreach (var target in targets)
		{
			float currentDistance = Vector3.Distance(target.transform.localPosition, transform.localPosition);
			if (currentDistance < distance && target.GetComponent<Target>().GetCarried() == 0 && target.GetComponent<Target>().GetInBase() != team)
			{
				distance = currentDistance;
				nearestTarget = target;
			}
		}
		return nearestTarget;
	}

	private float GetYAngle(GameObject target)
	{

		Vector3 targetDir = target.transform.position - transform.position;
		Vector3 forward = transform.forward;

		float angle = Vector3.SignedAngle(targetDir, forward, Vector3.up);
		return angle;
	}
}
